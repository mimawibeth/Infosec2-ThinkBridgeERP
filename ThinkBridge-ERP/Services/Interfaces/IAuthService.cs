using ThinkBridge_ERP.Models.Entities;
using Task = System.Threading.Tasks.Task;

namespace ThinkBridge_ERP.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResult> AuthenticateAsync(string email, string password, string? ipAddress = null);
    Task<User?> GetUserByIdAsync(int userId);
    Task<User?> GetUserByEmailAsync(string email);
    Task UpdateLastLoginAsync(int userId);
    Task<IList<string>> GetUserRolesAsync(int userId);
    string GetDashboardByRole(string primaryRole);
}

public class AuthResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public User? User { get; set; }
    public IList<string> Roles { get; set; } = new List<string>();
    public string? RedirectUrl { get; set; }
    public bool MustChangePassword { get; set; }
    public int LockoutSeconds { get; set; }
    public bool CanRenewSubscription { get; set; }
    public int? RenewalSubscriptionId { get; set; }
    public int? RenewalUserId { get; set; }
}
