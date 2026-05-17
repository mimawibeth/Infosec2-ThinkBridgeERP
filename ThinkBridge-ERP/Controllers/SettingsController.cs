using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Configuration;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services;
using Task = System.Threading.Tasks.Task;

namespace ThinkBridge_ERP.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private const string SecurityLogType = "Security";
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SettingsController> _logger;
    private readonly PasswordValidationService _passwordValidationService;
    private readonly IConfiguration _configuration;

    public SettingsController(
        ApplicationDbContext context,
        ILogger<SettingsController> logger,
        PasswordValidationService passwordValidationService,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _passwordValidationService = passwordValidationService;
        _configuration = configuration;
    }

    private int GetUserId() =>
        int.Parse(User.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);

    // GET api/settings/profile
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userId = GetUserId();
            var user = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.UserID == userId);

            if (user == null)
                return NotFound(new { message = "User not found." });

            // Get user roles
            var roles = await _context.UserRoles
                .Where(ur => ur.UserID == userId)
                .Include(ur => ur.Role)
                .Select(ur => ur.Role.RoleName)
                .ToListAsync();

            var primaryRole = user.IsSuperAdmin ? "SuperAdmin" :
                roles.Contains("CompanyAdmin") ? "CompanyAdmin" :
                roles.Contains("ProjectManager") ? "ProjectManager" : "TeamMember";

            return Ok(new
            {
                userId = user.UserID,
                fname = user.Fname,
                lname = user.Lname,
                email = user.Email,
                phone = user.Phone,
                avatarUrl = user.AvatarUrl,
                avatarColor = user.AvatarColor,
                status = user.Status,
                companyName = user.Company?.CompanyName,
                companyId = user.CompanyID,
                role = primaryRole,
                roles = roles,
                isSuperAdmin = user.IsSuperAdmin,
                lastLoginAt = user.LastLoginAt,
                createdAt = user.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching profile");
            return StatusCode(500, new { message = "An error occurred while fetching profile." });
        }
    }

    // PUT api/settings/profile
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        try
        {
            var userId = GetUserId();
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
                return NotFound(new { message = "User not found." });

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Fname) || string.IsNullOrWhiteSpace(request.Lname))
                return BadRequest(new { message = "First name and last name are required." });

            if (request.Fname.Length > 150)
                return BadRequest(new { message = "First name must be 150 characters or less." });

            if (request.Lname.Length > 150)
                return BadRequest(new { message = "Last name must be 150 characters or less." });

            if (!string.IsNullOrWhiteSpace(request.Phone) && request.Phone.Length > 30)
                return BadRequest(new { message = "Phone number must be 30 characters or less." });

            user.Fname = request.Fname.Trim();
            user.Lname = request.Lname.Trim();
            user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

            // Update avatar color if provided and valid hex
            if (!string.IsNullOrWhiteSpace(request.AvatarColor) &&
                System.Text.RegularExpressions.Regex.IsMatch(request.AvatarColor, @"^#[0-9A-Fa-f]{6}$"))
            {
                user.AvatarColor = request.AvatarColor;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Profile updated for user {UserId}", userId);

            return Ok(new
            {
                success = true,
                message = "Profile updated successfully.",
                fname = user.Fname,
                lname = user.Lname,
                phone = user.Phone,
                avatarColor = user.AvatarColor
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            return StatusCode(500, new { message = "An error occurred while updating profile." });
        }
    }

    // POST api/settings/change-password
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                return BadRequest(new { message = "Current password is required." });

            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 12)
                return BadRequest(new { message = "Password must be at least 12 characters." });

            if (!request.NewPassword.Any(char.IsUpper))
                return BadRequest(new { message = "Password must include an uppercase letter." });

            if (!request.NewPassword.Any(char.IsLower))
                return BadRequest(new { message = "Password must include a lowercase letter." });

            if (!request.NewPassword.Any(char.IsDigit))
                return BadRequest(new { message = "Password must include a number." });

            if (!System.Text.RegularExpressions.Regex.IsMatch(request.NewPassword, @"[^a-zA-Z0-9]"))
                return BadRequest(new { message = "Password must include a special character." });

            if (_passwordValidationService.IsBlacklisted(request.NewPassword))
                return BadRequest(new { message = "This password is too common and has been blocked for security. Please choose a stronger password." });

            if (request.NewPassword != request.ConfirmPassword)
                return BadRequest(new { message = "New passwords do not match." });

            var userId = GetUserId();
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
                return NotFound(new { message = "User not found." });

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
                return BadRequest(new { message = "Current password is incorrect." });

            // Check new password is different
            if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.Password))
                return BadRequest(new { message = "New password must be different from current password." });

            // Hash and save
            user.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.MustChangePassword = false;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password changed via settings for user {UserId}", userId);

            return Ok(new { success = true, message = "Password changed successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, new { message = "An error occurred while changing password." });
        }
    }

    [HttpGet("totp/status")]
    public async Task<IActionResult> GetTotpStatus()
    {
        try
        {
            var userId = GetUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var isLocked = user.TotpLockoutUntil.HasValue && user.TotpLockoutUntil.Value > DateTime.UtcNow;
            var lockoutSeconds = isLocked
                ? (int)Math.Ceiling((user.TotpLockoutUntil!.Value - DateTime.UtcNow).TotalSeconds)
                : 0;

            return Ok(new
            {
                isEnabled = user.IsTotpEnabled,
                isPendingSetup = !user.IsTotpEnabled && !string.IsNullOrWhiteSpace(user.TotpSecret),
                hasBackupCodes = TotpService.CountBackupCodes(user.TotpBackupCodes) > 0,
                lockoutSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching TOTP status");
            return StatusCode(500, new { message = "An error occurred while fetching TOTP status." });
        }
    }

    [HttpPost("totp/setup")]
    public async Task<IActionResult> SetupTotp()
    {
        try
        {
            var userId = GetUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var settings = GetTotpSettings();
            var secret = TotpService.GenerateSecret();
            var provisioningUri = TotpService.BuildProvisioningUri(
                settings.Issuer,
                user.Email,
                secret,
                settings.Digits,
                settings.PeriodSeconds);

            user.IsTotpEnabled = false;
            user.TotpSecret = secret;
            user.TotpBackupCodes = null;
            user.TotpFailedAttempts = 0;
            user.TotpLockoutUntil = null;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                secret,
                provisioningUri,
                issuer = settings.Issuer,
                account = user.Email,
                digits = settings.Digits,
                periodSeconds = settings.PeriodSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preparing TOTP setup");
            return StatusCode(500, new { message = "Unable to prepare TOTP setup right now." });
        }
    }

    [HttpPost("totp/verify-setup")]
    public async Task<IActionResult> VerifyTotpSetup([FromBody] TotpCodeRequest? request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new { message = "Verification code is required." });
            }

            var userId = GetUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (string.IsNullOrWhiteSpace(user.TotpSecret))
            {
                return BadRequest(new { message = "Start setup first to generate your authenticator secret." });
            }

            var settings = GetTotpSettings();
            var valid = TotpService.VerifyTotpCode(
                user.TotpSecret,
                request.Code,
                settings.Digits,
                settings.PeriodSeconds,
                settings.AllowedDriftWindows);

            if (!valid)
            {
                return BadRequest(new { message = "Invalid authenticator code. Please try again." });
            }

            var backupCodes = TotpService.GenerateBackupCodes(settings.BackupCodeCount, settings.BackupCodeLength);
            user.TotpBackupCodes = TotpService.SerializeHashedBackupCodes(backupCodes);
            user.IsTotpEnabled = true;
            user.TotpFailedAttempts = 0;
            user.TotpLockoutUntil = null;

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = user.UserID,
                CompanyID = user.CompanyID,
                Action = "TotpEnabled",
                EntityName = "User",
                EntityID = user.UserID,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                LogType = SecurityLogType,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                backupCodes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying TOTP setup");
            return StatusCode(500, new { message = "Unable to verify setup right now." });
        }
    }

    [HttpPost("totp/disable")]
    public async Task<IActionResult> DisableTotp([FromBody] TotpSensitiveActionRequest? request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new { message = "Current password and verification code are required." });
            }

            var userId = GetUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
            {
                return BadRequest(new { message = "Current password is incorrect." });
            }

            if (!user.IsTotpEnabled || string.IsNullOrWhiteSpace(user.TotpSecret))
            {
                return BadRequest(new { message = "Authenticator app is not enabled on this account." });
            }

            var settings = GetTotpSettings();
            var validTotp = TotpService.VerifyTotpCode(
                user.TotpSecret,
                request.Code,
                settings.Digits,
                settings.PeriodSeconds,
                settings.AllowedDriftWindows);

            if (!validTotp && !TotpService.TryConsumeBackupCode(request.Code, user.TotpBackupCodes, out var updatedCodes))
            {
                return BadRequest(new { message = "Invalid verification code." });
            }

            user.IsTotpEnabled = false;
            user.TotpSecret = null;
            user.TotpBackupCodes = null;
            user.TotpFailedAttempts = 0;
            user.TotpLockoutUntil = null;

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = user.UserID,
                CompanyID = user.CompanyID,
                Action = "TotpDisabled",
                EntityName = "User",
                EntityID = user.UserID,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                LogType = SecurityLogType,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Authenticator app has been disabled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling TOTP");
            return StatusCode(500, new { message = "Unable to disable TOTP right now." });
        }
    }

    [HttpPost("totp/backup-codes/regenerate")]
    public async Task<IActionResult> RegenerateBackupCodes([FromBody] TotpSensitiveActionRequest? request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new { message = "Current password and verification code are required." });
            }

            var userId = GetUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
            {
                return BadRequest(new { message = "Current password is incorrect." });
            }

            if (!user.IsTotpEnabled || string.IsNullOrWhiteSpace(user.TotpSecret))
            {
                return BadRequest(new { message = "Enable authenticator app before regenerating backup codes." });
            }

            var settings = GetTotpSettings();
            var validTotp = TotpService.VerifyTotpCode(
                user.TotpSecret,
                request.Code,
                settings.Digits,
                settings.PeriodSeconds,
                settings.AllowedDriftWindows);

            if (!validTotp)
            {
                if (TotpService.TryConsumeBackupCode(request.Code, user.TotpBackupCodes, out var updatedCodes))
                {
                    user.TotpBackupCodes = updatedCodes;
                }
                else
                {
                    return BadRequest(new { message = "Invalid verification code." });
                }
            }

            var backupCodes = TotpService.GenerateBackupCodes(settings.BackupCodeCount, settings.BackupCodeLength);
            user.TotpBackupCodes = TotpService.SerializeHashedBackupCodes(backupCodes);

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = user.UserID,
                CompanyID = user.CompanyID,
                Action = "TotpBackupCodesRegenerated",
                EntityName = "User",
                EntityID = user.UserID,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                LogType = SecurityLogType,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                backupCodes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error regenerating TOTP backup codes");
            return StatusCode(500, new { message = "Unable to regenerate backup codes right now." });
        }
    }

    // GET api/settings/onboarding
    [HttpGet("onboarding")]
    public async Task<IActionResult> GetOnboardingStatus()
    {
        try
        {
            var userId = GetUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound(new { message = "User not found." });
            return Ok(new { hasCompleted = user.HasCompletedOnboarding });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching onboarding status");
            return StatusCode(500, new { message = "An error occurred." });
        }
    }

    // POST api/settings/onboarding/complete
    [HttpPost("onboarding/complete")]
    public async Task<IActionResult> CompleteOnboarding()
    {
        try
        {
            var userId = GetUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound(new { message = "User not found." });
            user.HasCompletedOnboarding = true;
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing onboarding");
            return StatusCode(500, new { message = "An error occurred." });
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
            BackupCodeCount = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:BackupCodeCount") ?? 8, 4, 20),
            BackupCodeLength = Math.Clamp(_configuration.GetValue<int?>("Security:Totp:BackupCodeLength") ?? 10, 8, 16)
        };
    }
}

// DTOs
public class UpdateProfileRequest
{
    public string Fname { get; set; } = string.Empty;
    public string Lname { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarColor { get; set; }
}

public class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class TotpCodeRequest
{
    public string Code { get; set; } = string.Empty;
}

public class TotpSensitiveActionRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
