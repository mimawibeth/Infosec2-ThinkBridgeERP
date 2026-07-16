using System.Security.Claims;
using SecurityClaim = System.Security.Claims.Claim;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Configuration;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services;
using ThinkBridge_ERP.Services.Interfaces;

namespace ThinkBridge_ERP.Controllers;

public class AuthController : Controller
{
    private readonly IAuthService _authService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AuthController> _logger;
    private readonly IEmailService _emailService;
    private readonly IPayMongoService _payMongoService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly PasswordValidationService _passwordValidationService;
    private readonly IDataProtector _renewalAccessProtector;
    private readonly IDataProtector _mfaChallengeProtector;
    private const int RenewalAccessTokenMinutes = 20;
    private const string SecurityLogType = "Security";

    public AuthController(
        IAuthService authService,
        ApplicationDbContext context,
        ILogger<AuthController> logger,
        IEmailService emailService,
        IPayMongoService payMongoService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        PasswordValidationService passwordValidationService,
        IDataProtectionProvider dataProtectionProvider)
    {
        _authService = authService;
        _context = context;
        _logger = logger;
        _emailService = emailService;
        _payMongoService = payMongoService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _passwordValidationService = passwordValidationService;
        _renewalAccessProtector = dataProtectionProvider.CreateProtector("ThinkBridgeERP.Auth.RenewalAccess.v1");
        _mfaChallengeProtector = dataProtectionProvider.CreateProtector("ThinkBridgeERP.Auth.MfaChallenge.v1");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // If already authenticated, redirect to dashboard
        if (User.Identity?.IsAuthenticated == true)
        {
            var role = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "TeamMember";
            return Redirect(_authService.GetDashboardByRole(role));
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View("~/Views/Web/Login.cshtml");
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        try
        {
            _logger.LogInformation("Login attempt received");

            if (request == null)
            {
                _logger.LogWarning("Login request body is null");
                return Json(new { success = false, message = "Invalid request." });
            }

            var loginRequest = request;

            if (string.IsNullOrWhiteSpace(loginRequest.Email) || string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                return Json(new { success = false, message = "Email and password are required." });
            }

            var normalizedLoginEmail = loginRequest.Email.Trim();

            var loginCaptchaValid = await VerifyTurnstileAsync(loginRequest.CaptchaToken);
            if (!loginCaptchaValid)
            {
                return Json(new { success = false, message = "Verification failed. Please try again." });
            }

            _logger.LogInformation("Authenticating user: {Email}", loginRequest.Email);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _authService.AuthenticateAsync(normalizedLoginEmail, loginRequest.Password, ipAddress);

            if (!result.Success)
            {
                string? renewalToken = null;
                if (result.ErrorCode == "subscription_expired"
                    && result.CanRenewSubscription
                    && result.RenewalSubscriptionId.HasValue
                    && result.RenewalUserId.HasValue)
                {
                    renewalToken = CreateRenewalAccessToken(result.RenewalSubscriptionId.Value, result.RenewalUserId.Value);
                }

                return Json(new
                {
                    success = false,
                    message = result.ErrorMessage,
                    errorCode = result.ErrorCode,
                    lockoutSeconds = result.LockoutSeconds,
                    canRenewSubscription = result.CanRenewSubscription,
                    renewalToken
                });
            }

            if (result.User == null)
            {
                return Json(new { success = false, message = "Unable to complete login. Please try again." });
            }

            var totpSettings = GetTotpSettings();
            var challengeToken = CreateMfaChallengeToken(result.User.UserID, loginRequest.RememberMe, totpSettings.ChallengeTokenMinutes);

            // Enforce mandatory authenticator setup before issuing an authenticated session.
            if (!result.User.IsTotpEnabled || string.IsNullOrWhiteSpace(result.User.TotpSecret))
            {
                if (string.IsNullOrWhiteSpace(result.User.TotpSecret))
                {
                    result.User.TotpSecret = TotpService.GenerateSecret();
                }

                result.User.IsTotpEnabled = false;
                result.User.TotpBackupCodes = null;
                result.User.TotpFailedAttempts = 0;
                result.User.TotpLockoutUntil = null;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = false,
                    requiresTotp = true,
                    requiresTotpEnrollment = true,
                    mfaToken = challengeToken,
                    message = "Set up your authenticator app to continue.",
                    setup = BuildTotpSetupPayload(result.User, result.User.TotpSecret!, totpSettings)
                });
            }

            if (result.User.IsTotpEnabled)
            {
                if (result.User.TotpLockoutUntil.HasValue && result.User.TotpLockoutUntil.Value > DateTime.UtcNow)
                {
                    var lockoutSeconds = (int)Math.Ceiling((result.User.TotpLockoutUntil.Value - DateTime.UtcNow).TotalSeconds);
                    return Json(new
                    {
                        success = false,
                        requiresTotp = true,
                        lockoutSeconds,
                        message = "Authenticator verification is temporarily locked. Please try again shortly."
                    });
                }

                return Json(new
                {
                    success = false,
                    requiresTotp = true,
                    mfaToken = challengeToken,
                    message = "Enter the code from your authenticator app to continue."
                });
            }

            var principalBundle = await BuildPrincipalAsync(result.User);
            await SignInWithPrincipalAsync(principalBundle.Principal, loginRequest.RememberMe);

            // Update last login time only after final authentication step succeeds.
            await _authService.UpdateLastLoginAsync(result.User.UserID);

            _logger.LogInformation("User {Email} signed in successfully", loginRequest.Email);

            return Json(new
            {
                success = true,
                redirectUrl = result.MustChangePassword ? "/Auth/ChangePassword" : result.RedirectUrl,
                mustChangePassword = result.MustChangePassword,
                user = new
                {
                    name = $"{result.User.Fname} {result.User.Lname}",
                    email = result.User.Email,
                    role = principalBundle.PrimaryRole
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {Email}", request?.Email);
            return Json(new { success = false, message = "An error occurred during login. Please try again." });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("/Auth/EnrollTotp")]
    public async Task<IActionResult> EnrollTotp([FromBody] VerifyTotpRequest? request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MfaToken) || string.IsNullOrWhiteSpace(request.Code))
            {
                return Json(new { success = false, message = "Verification code is required." });
            }

            if (!TryReadMfaChallengeToken(request.MfaToken, out var challenge))
            {
                return Json(new { success = false, message = "Your setup session has expired. Please sign in again." });
            }

            var user = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.UserID == challenge.UserId);

            if (user == null)
            {
                return Json(new { success = false, message = "Unable to find this account. Please sign in again." });
            }

            if (user.IsTotpEnabled && !string.IsNullOrWhiteSpace(user.TotpSecret))
            {
                return Json(new { success = false, message = "Authenticator is already enabled. Please sign in again." });
            }

            if (string.IsNullOrWhiteSpace(user.TotpSecret))
            {
                return Json(new { success = false, message = "Setup secret was not found. Please sign in again to restart setup." });
            }

            var totpSettings = GetTotpSettings();
            var isValid = TotpService.VerifyTotpCode(
                user.TotpSecret,
                request.Code,
                totpSettings.Digits,
                totpSettings.PeriodSeconds,
                totpSettings.AllowedDriftWindows);

            if (!isValid)
            {
                return Json(new { success = false, message = "Invalid authenticator code. Please try again." });
            }

            var backupCodes = TotpService.GenerateBackupCodes(totpSettings.BackupCodeCount, totpSettings.BackupCodeLength);
            user.TotpBackupCodes = TotpService.SerializeHashedBackupCodes(backupCodes);
            user.IsTotpEnabled = true;
            user.TotpFailedAttempts = 0;
            user.TotpLockoutUntil = null;

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = user.UserID,
                CompanyID = user.CompanyID,
                Action = "TotpEnabledDuringLogin",
                EntityName = "User",
                EntityID = user.UserID,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                LogType = SecurityLogType,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            var principalBundle = await BuildPrincipalAsync(user);
            await SignInWithPrincipalAsync(principalBundle.Principal, challenge.RememberMe);
            await _authService.UpdateLastLoginAsync(user.UserID);

            return Json(new
            {
                success = true,
                backupCodes,
                redirectUrl = user.MustChangePassword
                    ? "/Auth/ChangePassword"
                    : _authService.GetDashboardByRole(principalBundle.PrimaryRole),
                mustChangePassword = user.MustChangePassword,
                user = new
                {
                    name = $"{user.Fname} {user.Lname}",
                    email = user.Email,
                    role = principalBundle.PrimaryRole
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing mandatory TOTP enrollment");
            return Json(new { success = false, message = "Unable to complete setup right now. Please try again." });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("/Auth/VerifyTotp")]
    public async Task<IActionResult> VerifyTotp([FromBody] VerifyTotpRequest? request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MfaToken) || string.IsNullOrWhiteSpace(request.Code))
            {
                return Json(new { success = false, message = "Verification code is required." });
            }

            if (!TryReadMfaChallengeToken(request.MfaToken, out var challenge))
            {
                return Json(new { success = false, message = "Your verification session has expired. Please sign in again." });
            }

            var user = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.UserID == challenge.UserId);

            if (user == null || !user.IsTotpEnabled || string.IsNullOrWhiteSpace(user.TotpSecret))
            {
                return Json(new { success = false, message = "Two-factor authentication is not available for this account." });
            }

            var totpSettings = GetTotpSettings();
            if (user.TotpLockoutUntil.HasValue && user.TotpLockoutUntil.Value > DateTime.UtcNow)
            {
                var lockoutSeconds = (int)Math.Ceiling((user.TotpLockoutUntil.Value - DateTime.UtcNow).TotalSeconds);
                return Json(new
                {
                    success = false,
                    lockoutSeconds,
                    message = "Authenticator verification is temporarily locked. Please try again shortly."
                });
            }

            var isTotpValid = TotpService.VerifyTotpCode(
                user.TotpSecret,
                request.Code,
                totpSettings.Digits,
                totpSettings.PeriodSeconds,
                totpSettings.AllowedDriftWindows);

            var usedBackupCode = false;
            if (!isTotpValid)
            {
                if (TotpService.TryConsumeBackupCode(request.Code, user.TotpBackupCodes, out var updatedBackupCodes))
                {
                    usedBackupCode = true;
                    user.TotpBackupCodes = updatedBackupCodes;
                }
                else
                {
                    user.TotpFailedAttempts++;
                    if (user.TotpFailedAttempts >= totpSettings.MaxFailedAttempts)
                    {
                        user.TotpFailedAttempts = 0;
                        user.TotpLockoutUntil = DateTime.UtcNow.AddMinutes(totpSettings.LockoutMinutes);

                        _context.AuditLogs.Add(new AuditLog
                        {
                            UserID = user.UserID,
                            CompanyID = user.CompanyID,
                            Action = "TotpLockoutTriggered",
                            EntityName = "User",
                            EntityID = user.UserID,
                            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                            LogType = SecurityLogType,
                            CreatedAt = DateTime.UtcNow
                        });

                        await _context.SaveChangesAsync();

                        var lockoutSeconds = (int)Math.Ceiling(TimeSpan.FromMinutes(totpSettings.LockoutMinutes).TotalSeconds);
                        return Json(new
                        {
                            success = false,
                            lockoutSeconds,
                            message = "Too many invalid codes. Verification is temporarily locked."
                        });
                    }

                    await _context.SaveChangesAsync();
                    return Json(new { success = false, message = "Invalid verification code. Please try again." });
                }
            }

            user.TotpFailedAttempts = 0;
            user.TotpLockoutUntil = null;

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = user.UserID,
                CompanyID = user.CompanyID,
                Action = usedBackupCode ? "TotpVerifiedWithBackupCode" : "TotpVerified",
                EntityName = "User",
                EntityID = user.UserID,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                LogType = SecurityLogType,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            var principalBundle = await BuildPrincipalAsync(user);
            await SignInWithPrincipalAsync(principalBundle.Principal, challenge.RememberMe);
            await _authService.UpdateLastLoginAsync(user.UserID);

            return Json(new
            {
                success = true,
                redirectUrl = user.MustChangePassword
                    ? "/Auth/ChangePassword"
                    : _authService.GetDashboardByRole(principalBundle.PrimaryRole),
                mustChangePassword = user.MustChangePassword,
                user = new
                {
                    name = $"{user.Fname} {user.Lname}",
                    email = user.Email,
                    role = principalBundle.PrimaryRole
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying TOTP challenge");
            return Json(new { success = false, message = "Unable to verify code right now. Please try again." });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation("User {Email} signed out", email);

        return Json(new { success = true, redirectUrl = "/Auth/Login" });
    }

    [HttpGet]
    [Authorize]
    [Route("/Auth/Logout")]
    public async Task<IActionResult> LogoutGet()
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation("User {Email} signed out", email);

        return Redirect("/Auth/Login");
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 12)
            {
                return Json(new { success = false, message = "Password must be at least 12 characters." });
            }

            if (!request.NewPassword.Any(char.IsUpper))
                return Json(new { success = false, message = "Password must include an uppercase letter." });

            if (!request.NewPassword.Any(char.IsLower))
                return Json(new { success = false, message = "Password must include a lowercase letter." });

            if (!request.NewPassword.Any(char.IsDigit))
                return Json(new { success = false, message = "Password must include a number." });

            if (!System.Text.RegularExpressions.Regex.IsMatch(request.NewPassword, @"[^a-zA-Z0-9]"))
                return Json(new { success = false, message = "Password must include a special character." });

            if (_passwordValidationService.IsBlacklisted(request.NewPassword))
                return Json(new { success = false, message = "This password is too common and has been blocked for security. Please choose a stronger password." });

            if (request.NewPassword != request.ConfirmPassword)
            {
                return Json(new { success = false, message = "Passwords do not match." });
            }

            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId) || userId == 0)
            {
                return Json(new { success = false, message = "Invalid user session. Please log in again." });
            }

            var user = await _authService.GetUserByIdAsync(userId);

            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
            {
                return Json(new { success = false, message = "Current password is incorrect." });
            }

            // Hash and save new password
            user.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.MustChangePassword = false;

            // Save to database
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password changed successfully for user {UserId}", userId);

            var role = User.Claims.FirstOrDefault(c => c.Type == "PrimaryRole")?.Value ?? "TeamMember";
            return Json(new { success = true, redirectUrl = _authService.GetDashboardByRole(role) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return Json(new { success = false, message = "An error occurred while changing password." });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("/Auth/ForgotPassword")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Email))
            {
                return Json(new { success = false, message = "Please enter your email address." });
            }

            var forgotCaptchaValid = await VerifyTurnstileAsync(request.CaptchaToken);
            if (!forgotCaptchaValid)
            {
                return Json(new { success = false, message = "Verification failed. Please try again." });
            }

            var user = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower().Trim() && !u.IsSuperAdmin);

            if (user == null)
            {
                // Return generic message to prevent email enumeration
                return Json(new { success = true, message = "If an account exists with that email, a password reset has been sent." });
            }

            // Generate temporary password and send email before persisting (avoid locking the account if email fails)
            var companyName = user.Company?.CompanyName ?? "ThinkBridge";
            var tempPassword = $"{companyName.Replace(" ", "")}_{Guid.NewGuid().ToString("N")[..6]}!";

            var emailResult = await _emailService.SendPasswordResetEmailAsync(user.Email, user.Fname, tempPassword);
            if (!emailResult.Success)
            {
                _logger.LogWarning("Failed to send password reset email to {Email}: {Error}", user.Email, emailResult.ErrorMessage);
                return Json(new { success = false, message = emailResult.ErrorMessage ?? "Unable to send the reset email. Please try again later." });
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            user.MustChangePassword = true;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password reset for user {Email} via forgot password", user.Email);

            return Json(new { success = true, message = "A temporary password has been sent to your email. Please check your inbox." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing forgot password for {Email}", request?.Email);
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("/Auth/CreateRenewalCheckout")]
    public async Task<IActionResult> CreateRenewalCheckout([FromBody] RenewalCheckoutRequest? request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.RenewalToken))
            {
                return Json(new { success = false, message = "Your renewal session is invalid. Please sign in again." });
            }

            if (!TryReadRenewalAccessToken(request.RenewalToken, out var payload))
            {
                return Json(new { success = false, message = "Your renewal session has expired. Please sign in again." });
            }

            var subscription = await _context.Subscriptions
                .Include(s => s.Plan)
                .Include(s => s.Company)
                .FirstOrDefaultAsync(s => s.SubscriptionID == payload.SubscriptionId);

            if (subscription == null)
            {
                return Json(new { success = false, message = "Unable to load subscription for renewal." });
            }

            var renewableStatuses = new[] { "Expired", "GracePeriod", "Active" };
            if (!renewableStatuses.Contains(subscription.Status))
            {
                return Json(new { success = false, message = "This subscription is not eligible for renewal right now." });
            }

            if (subscription.Plan.Price <= 0)
            {
                return Json(new { success = false, message = "This plan does not require a paid renewal." });
            }

            var renewalUser = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserID == payload.UserId && u.CompanyID == subscription.CompanyID);

            if (renewalUser == null)
            {
                return Json(new { success = false, message = "Unable to validate renewal access." });
            }

            var isAuthorizedRenewer = renewalUser.IsSuperAdmin
                || renewalUser.UserRoles.Any(ur => ur.Role.RoleName == "CompanyAdmin" || ur.Role.RoleName == "SuperAdmin");
            if (!isAuthorizedRenewer)
            {
                return Json(new { success = false, message = "Only company administrators can renew this subscription." });
            }

            var checkoutResult = await _payMongoService.CreateCheckoutSessionAsync(new PayMongoCheckoutRequest
            {
                SubscriptionId = subscription.SubscriptionID,
                Amount = subscription.Plan.Price,
                Description = $"ThinkBridge ERP - {subscription.Plan.PlanName} Plan Renewal (Monthly)",
                CompanyName = subscription.Company.CompanyName,
                CustomerEmail = renewalUser.Email,
                BaseUrl = $"{Request.Scheme}://{Request.Host}"
            });

            if (!checkoutResult.Success || string.IsNullOrWhiteSpace(checkoutResult.CheckoutUrl))
            {
                _logger.LogWarning("Failed to create renewal checkout from login recovery flow for subscription {SubscriptionId}", subscription.SubscriptionID);
                return Json(new { success = false, message = checkoutResult.ErrorMessage ?? "Unable to start renewal checkout right now." });
            }

            return Json(new
            {
                success = true,
                checkoutUrl = checkoutResult.CheckoutUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating renewal checkout from login recovery flow");
            return Json(new { success = false, message = "Unable to start renewal right now. Please try again." });
        }
    }

    [HttpPost]
    [Authorize(Policy = "SuperAdminOnly")]
    [ValidateAntiForgeryToken]
    [Route("/Auth/AdminResetLockout")]
    public async Task<IActionResult> AdminResetLockout([FromBody] AdminResetLockoutRequest? request)
    {
        try
        {
            if (request == null || (!request.UserId.HasValue && string.IsNullOrWhiteSpace(request.Email)))
            {
                return Json(new { success = false, message = "Please provide a user ID or email." });
            }

            var adminUserIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(adminUserIdClaim, out var adminUserId) || adminUserId == 0)
            {
                return Json(new { success = false, message = "Unable to identify administrator account." });
            }

            User? targetUser;
            if (request.UserId.HasValue)
            {
                targetUser = await _context.Users.FirstOrDefaultAsync(u => u.UserID == request.UserId.Value);
            }
            else
            {
                var normalizedEmail = request.Email!.Trim().ToLower();
                targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
            }

            if (targetUser == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            targetUser.FailedLoginAttempts = 0;
            targetUser.LockoutEnd = null;
            targetUser.LockoutLevel = 0;
            targetUser.IsPermanentlyLocked = false;
            targetUser.PermanentlyLockedAt = null;

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = adminUserId,
                CompanyID = targetUser.CompanyID,
                Action = "ResetUserLockout",
                EntityName = "User",
                EntityID = targetUser.UserID,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                LogType = "Security",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation("SuperAdmin {AdminUserId} reset lockout state for user {TargetUserId}", adminUserId, targetUser.UserID);
            return Json(new
            {
                success = true,
                message = "User lockout state has been reset.",
                userId = targetUser.UserID,
                email = targetUser.Email
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting user lockout state");
            return Json(new { success = false, message = "Unable to reset lockout state right now." });
        }
    }

    private async Task<(ClaimsPrincipal Principal, string PrimaryRole)> BuildPrincipalAsync(User user)
    {
        var roles = await _context.UserRoles
            .Where(ur => ur.UserID == user.UserID)
            .Include(ur => ur.Role)
            .Select(ur => ur.Role.RoleName)
            .ToListAsync();

        if (user.IsSuperAdmin && !roles.Contains("SuperAdmin"))
        {
            roles.Add("SuperAdmin");
        }

        var claims = new List<SecurityClaim>
        {
            new(ClaimTypes.NameIdentifier, user.UserID.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, $"{user.Fname} {user.Lname}"),
            new("FirstName", user.Fname),
            new("LastName", user.Lname),
            new("IsSuperAdmin", user.IsSuperAdmin.ToString()),
            new("AvatarUrl", user.AvatarUrl ?? string.Empty),
            new("AvatarColor", user.AvatarColor ?? "#0B4F6C"),
            new("HasCompletedOnboarding", user.HasCompletedOnboarding.ToString())
        };

        if (user.CompanyID.HasValue)
        {
            claims.Add(new SecurityClaim("CompanyID", user.CompanyID.Value.ToString()));
            claims.Add(new SecurityClaim("CompanyName", user.Company?.CompanyName ?? string.Empty));

            try
            {
                var subscription = await _context.Subscriptions
                    .Where(s => s.CompanyID == user.CompanyID.Value)
                    .OrderByDescending(s => s.StartDate)
                    .FirstOrDefaultAsync();
                if (subscription != null)
                {
                    claims.Add(new SecurityClaim("SubscriptionStatus", subscription.Status));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load subscription status claim for user {Email}", user.Email);
            }
        }

        foreach (var role in roles)
        {
            claims.Add(new SecurityClaim(ClaimTypes.Role, role));
        }

        var primaryRole = roles.Contains("SuperAdmin") ? "SuperAdmin" :
            roles.Contains("CompanyAdmin") ? "CompanyAdmin" :
            roles.Contains("ProjectManager") ? "ProjectManager" : "TeamMember";
        claims.Add(new SecurityClaim("PrimaryRole", primaryRole));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return (new ClaimsPrincipal(identity), primaryRole);
    }

    private async System.Threading.Tasks.Task SignInWithPrincipalAsync(ClaimsPrincipal principal, bool rememberMe)
    {
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null,
            AllowRefresh = true
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProperties);
    }

    private string CreateMfaChallengeToken(int userId, bool rememberMe, int ttlMinutes)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes);
        var payload = new MfaChallengePayload(userId, rememberMe, expiresAt);
        return _mfaChallengeProtector.Protect($"{payload.UserId}|{payload.RememberMe}|{payload.ExpiresAt.Ticks}");
    }

    private bool TryReadMfaChallengeToken(string token, out MfaChallengePayload payload)
    {
        payload = default;

        try
        {
            var raw = _mfaChallengeProtector.Unprotect(token);
            var parts = raw.Split('|');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var userId)
                || !bool.TryParse(parts[1], out var rememberMe)
                || !long.TryParse(parts[2], out var ticks))
            {
                return false;
            }

            var expiresAt = new DateTime(ticks, DateTimeKind.Utc);
            if (expiresAt <= DateTime.UtcNow)
            {
                return false;
            }

            payload = new MfaChallengePayload(userId, rememberMe, expiresAt);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private TotpSettings GetTotpSettings()
    {
        return new TotpSettings
        {
            Issuer = _configuration["Security:Totp:Issuer"] ?? "ThinkBridge ERP",
            Digits = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:Digits") ?? 6, 6, 8),
            PeriodSeconds = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:PeriodSeconds") ?? 30, 15, 90),
            AllowedDriftWindows = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:AllowedDriftWindows") ?? 1, 0, 5),
            MaxFailedAttempts = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:MaxFailedAttempts") ?? 5, 3, 10),
            LockoutMinutes = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:LockoutMinutes") ?? 10, 1, 60),
            ChallengeTokenMinutes = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:ChallengeTokenMinutes") ?? 10, 2, 30),
            BackupCodeCount = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:BackupCodeCount") ?? 8, 4, 20),
            BackupCodeLength = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:BackupCodeLength") ?? 10, 8, 16)
        };
    }

    private object BuildTotpSetupPayload(User user, string secret, TotpSettings settings)
    {
        var provisioningUri = TotpService.BuildProvisioningUri(
            settings.Issuer,
            user.Email,
            secret,
            settings.Digits,
            settings.PeriodSeconds);

        return new
        {
            secret,
            provisioningUri,
            issuer = settings.Issuer,
            account = user.Email,
            digits = settings.Digits,
            periodSeconds = settings.PeriodSeconds
        };
    }

    private async Task<bool> VerifyTurnstileAsync(string? captchaToken)
    {
        var secretKey = _configuration["Turnstile:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            _logger.LogWarning("Turnstile secret key is not configured. Skipping CAPTCHA verification.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(captchaToken))
        {
            _logger.LogWarning("Turnstile token is missing.");
            return false;
        }

        var client = _httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = captchaToken,
            ["remoteip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty
        });

        using var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", content);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Turnstile verification endpoint returned status code {StatusCode}", response.StatusCode);
            return false;
        }

        var verificationResult = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>();
        if (verificationResult?.Success == true)
        {
            return true;
        }

        var errorCodes = verificationResult?.ErrorCodes == null
            ? "none"
            : string.Join(",", verificationResult.ErrorCodes);
        _logger.LogWarning("Turnstile verification failed. Error codes: {ErrorCodes}", errorCodes);

        return false;
    }

    private string CreateRenewalAccessToken(int subscriptionId, int userId)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(RenewalAccessTokenMinutes);
        var payload = $"{subscriptionId}|{userId}|{expiresAt.Ticks}";
        return _renewalAccessProtector.Protect(payload);
    }

    private bool TryReadRenewalAccessToken(string token, out RenewalAccessPayload payload)
    {
        payload = default;

        try
        {
            var unprotected = _renewalAccessProtector.Unprotect(token);
            var parts = unprotected.Split('|');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var subscriptionId)
                || !int.TryParse(parts[1], out var userId)
                || !long.TryParse(parts[2], out var expirationTicks))
            {
                return false;
            }

            var expiresAt = new DateTime(expirationTicks, DateTimeKind.Utc);
            if (expiresAt < DateTime.UtcNow)
            {
                return false;
            }

            payload = new RenewalAccessPayload(subscriptionId, userId, expiresAt);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public readonly record struct RenewalAccessPayload(int SubscriptionId, int UserId, DateTime ExpiresAt);

public readonly record struct MfaChallengePayload(int UserId, bool RememberMe, DateTime ExpiresAt);

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public string CaptchaToken { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string CaptchaToken { get; set; } = string.Empty;
}

public class RenewalCheckoutRequest
{
    public string RenewalToken { get; set; } = string.Empty;
}

public class AdminResetLockoutRequest
{
    public int? UserId { get; set; }
    public string? Email { get; set; }
}

public class VerifyTotpRequest
{
    public string MfaToken { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class TurnstileVerifyResponse
{
    public bool Success { get; set; }

    [JsonPropertyName("error-codes")]
    public List<string>? ErrorCodes { get; set; }
}
