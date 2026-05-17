using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace ThinkBridge_ERP.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttemptsPerStage = 5;
    private const int FirstLockoutMinutes = 5;
    private const int SecondLockoutMinutes = 10;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<AuthService> _logger;

    public AuthService(ApplicationDbContext context, ILogger<AuthService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(string email, string password, string? ipAddress = null)
    {
        try
        {
            var normalizedEmail = email.Trim().ToLower();

            // Find user by email
            var user = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null)
            {
                _logger.LogWarning("Login attempt failed: User not found for email {Email}", email);
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Invalid email or password.",
                    ErrorCode = "invalid_credentials"
                };
            }

            if (user.IsPermanentlyLocked)
            {
                await TryWriteSecurityAuditAsync(user, "LoginBlockedPermanentLockout", ipAddress);
                _logger.LogWarning("Login attempt failed: User {Email} is permanently locked", email);
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Your account has been permanently locked. Please contact your administrator.",
                    ErrorCode = "permanent_lockout"
                };
            }

            // Check if account is locked out
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
            {
                var remaining = Math.Max(1, (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.UtcNow).TotalSeconds));
                await TryWriteSecurityAuditAsync(user, "LoginBlockedTemporaryLockout", ipAddress);
                _logger.LogWarning("Login attempt failed: User {Email} is locked out for {Seconds}s", email, remaining);
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Account is temporarily locked.",
                    ErrorCode = "temporary_lockout",
                    LockoutSeconds = remaining
                };
            }

            // Verify password using BCrypt
            if (!BCrypt.Net.BCrypt.Verify(password, user.Password))
            {
                user.FailedLoginAttempts++;

                if (user.FailedLoginAttempts >= MaxFailedAttemptsPerStage)
                {
                    if (user.LockoutLevel <= 0)
                    {
                        user.LockoutLevel = 1;
                        user.LockoutEnd = DateTime.UtcNow.AddMinutes(FirstLockoutMinutes);
                        user.FailedLoginAttempts = 0;

                        await _context.SaveChangesAsync();
                        await TryWriteSecurityAuditAsync(user, "LoginLockoutLevel1Triggered", ipAddress);

                        var firstLockoutSecs = Math.Max(1, (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.UtcNow).TotalSeconds));
                        _logger.LogWarning("User {Email} entered first lockout stage for {Minutes} minutes", email, FirstLockoutMinutes);

                        return new AuthResult
                        {
                            Success = false,
                            ErrorMessage = "Account is temporarily locked due to multiple failed attempts.",
                            ErrorCode = "temporary_lockout",
                            LockoutSeconds = firstLockoutSecs
                        };
                    }

                    if (user.LockoutLevel == 1)
                    {
                        user.LockoutLevel = 2;
                        user.LockoutEnd = DateTime.UtcNow.AddMinutes(SecondLockoutMinutes);
                        user.FailedLoginAttempts = 0;

                        await _context.SaveChangesAsync();
                        await TryWriteSecurityAuditAsync(user, "LoginLockoutLevel2Triggered", ipAddress);

                        var secondLockoutSecs = Math.Max(1, (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.UtcNow).TotalSeconds));
                        _logger.LogWarning("User {Email} entered second lockout stage for {Minutes} minutes", email, SecondLockoutMinutes);

                        return new AuthResult
                        {
                            Success = false,
                            ErrorMessage = "Account is temporarily locked due to multiple failed attempts.",
                            ErrorCode = "temporary_lockout",
                            LockoutSeconds = secondLockoutSecs
                        };
                    }

                    user.LockoutLevel = 3;
                    user.IsPermanentlyLocked = true;
                    user.PermanentlyLockedAt = DateTime.UtcNow;
                    user.LockoutEnd = null;
                    user.FailedLoginAttempts = 0;

                    await _context.SaveChangesAsync();
                    await TryWriteSecurityAuditAsync(user, "LoginPermanentLockoutTriggered", ipAddress);

                    _logger.LogWarning("User {Email} has been permanently locked after repeated failed login attempts", email);
                    return new AuthResult
                    {
                        Success = false,
                        ErrorMessage = "Your account has been permanently locked. Please contact your administrator.",
                        ErrorCode = "permanent_lockout"
                    };
                }

                await _context.SaveChangesAsync();
                await TryWriteSecurityAuditAsync(user, $"LoginFailedInvalidPasswordAttempt{user.FailedLoginAttempts}", ipAddress);

                var attemptsRemaining = Math.Max(0, MaxFailedAttemptsPerStage - user.FailedLoginAttempts);
                _logger.LogWarning("Login attempt failed: Invalid password for user {Email} (attempt {Attempts}, {Remaining} remaining before lockout)", email, user.FailedLoginAttempts, attemptsRemaining);

                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = attemptsRemaining > 0
                        ? $"Invalid email or password. {attemptsRemaining} attempt(s) remaining before temporary lockout."
                        : "Invalid email or password.",
                    ErrorCode = "invalid_credentials",
                    LockoutSeconds = 0
                };
            }

            // Check account status only after password verification to avoid exposing account state.
            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                if (user.CompanyID.HasValue)
                {
                    var subscription = await _context.Subscriptions
                        .Where(s => s.CompanyID == user.CompanyID.Value)
                        .OrderByDescending(s => s.StartDate)
                        .FirstOrDefaultAsync();

                    if (subscription != null && subscription.Status == "Expired")
                    {
                        var rolesForRenewal = await GetUserRolesAsync(user.UserID);
                        var canRenew = user.IsSuperAdmin || rolesForRenewal.Contains("SuperAdmin") || rolesForRenewal.Contains("CompanyAdmin");

                        _logger.LogWarning("Login blocked: User {Email} - subscription expired past grace period", email);
                        return new AuthResult
                        {
                            Success = false,
                            ErrorCode = "subscription_expired",
                            ErrorMessage = canRenew
                                ? "Your subscription has expired and the grace period has ended. Renew now to restore access."
                                : "Your subscription has expired and the grace period has ended. Please contact your company administrator to renew.",
                            CanRenewSubscription = canRenew,
                            RenewalSubscriptionId = canRenew ? subscription.SubscriptionID : null,
                            RenewalUserId = canRenew ? user.UserID : null
                        };
                    }
                }

                _logger.LogWarning("Login attempt failed: User {Email} is not active (Status: {Status})", email, user.Status);
                return new AuthResult
                {
                    Success = false,
                    ErrorCode = "account_inactive",
                    ErrorMessage = "Your account is not active. Please contact your administrator."
                };
            }

            // Reset failed attempts only after password and account status checks pass
            if (user.FailedLoginAttempts > 0 || user.LockoutEnd.HasValue)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = null;
                await _context.SaveChangesAsync();
            }

            // Get user roles
            var roles = await GetUserRolesAsync(user.UserID);

            // Determine primary role for dashboard redirect
            var primaryRole = DeterminePrimaryRole(roles, user.IsSuperAdmin);
            var redirectUrl = GetDashboardByRole(primaryRole);

            _logger.LogInformation("User {Email} logged in successfully with role {Role}", email, primaryRole);

            return new AuthResult
            {
                Success = true,
                User = user,
                Roles = roles,
                RedirectUrl = redirectUrl,
                MustChangePassword = user.MustChangePassword
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication for user {Email}", email);
            return new AuthResult
            {
                Success = false,
                ErrorCode = "server_error",
                ErrorMessage = "An error occurred during login. Please try again."
            };
        }
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _context.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.UserID == userId);
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLower();

        return await _context.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IList<string>> GetUserRolesAsync(int userId)
    {
        return await _context.UserRoles
            .Where(ur => ur.UserID == userId)
            .Include(ur => ur.Role)
            .Select(ur => ur.Role.RoleName)
            .ToListAsync();
    }

    public string GetDashboardByRole(string primaryRole)
    {
        return primaryRole.ToLower() switch
        {
            "superadmin" => "/Web/SuperAdminDashboard",
            "companyadmin" => "/Web/Dashboard",
            "projectmanager" => "/Web/ProjectManagerDashboard",
            "teammember" => "/Web/TeamMemberDashboard",
            _ => "/Web/Dashboard"
        };
    }

    private string DeterminePrimaryRole(IList<string> roles, bool isSuperAdmin)
    {
        // SuperAdmin takes priority
        if (isSuperAdmin || roles.Contains("SuperAdmin"))
            return "SuperAdmin";

        // Priority order: CompanyAdmin > ProjectManager > TeamMember
        if (roles.Contains("CompanyAdmin"))
            return "CompanyAdmin";

        if (roles.Contains("ProjectManager"))
            return "ProjectManager";

        if (roles.Contains("TeamMember"))
            return "TeamMember";

        // Default fallback
        return "TeamMember";
    }

    private async Task TryWriteSecurityAuditAsync(User user, string action, string? ipAddress)
    {
        var auditLog = new AuditLog
        {
            UserID = user.UserID,
            CompanyID = user.CompanyID,
            Action = action,
            EntityName = "User",
            EntityID = user.UserID,
            IPAddress = ipAddress,
            LogType = "Security",
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write security audit log for user {UserId} and action {Action}", user.UserID, action);
            _context.Entry(auditLog).State = EntityState.Detached;
        }
    }
}
