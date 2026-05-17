using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ThinkBridge_ERP.Controllers
{
    [Authorize]
    public class WebController : Controller
    {
        private readonly IConfiguration _configuration;

        public WebController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Helper method to set user role from authentication claims
        private void SetUserRoleFromClaims()
        {
            var primaryRole = User.Claims.FirstOrDefault(c => c.Type == "PrimaryRole")?.Value ?? "TeamMember";
            ViewData["UserRole"] = primaryRole.ToLower();
            ViewData["UserName"] = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            ViewData["UserEmail"] = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            ViewData["CompanyName"] = User.Claims.FirstOrDefault(c => c.Type == "CompanyName")?.Value;
            ViewData["AvatarUrl"] = User.Claims.FirstOrDefault(c => c.Type == "AvatarUrl")?.Value;
        }

        // Super Admin Dashboard
        [Authorize(Policy = "SuperAdminOnly")]
        public IActionResult SuperAdminDashboard()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Super Admin - Companies Management
        [Authorize(Policy = "SuperAdminOnly")]
        public IActionResult Companies()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Super Admin - Subscriptions
        [Authorize(Policy = "SuperAdminOnly")]
        public IActionResult Subscriptions()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Super Admin - Payments
        [Authorize(Policy = "SuperAdminOnly")]
        public IActionResult Payments()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Super Admin - Reports
        [Authorize(Policy = "SuperAdminOnly")]
        public IActionResult SuperAdminReports()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Super Admin - Security
        [Authorize(Policy = "SuperAdminOnly")]
        public IActionResult SuperAdminSecurity()
        {
            SetUserRoleFromClaims();

            var currentUserEmail = (ViewData["UserEmail"] as string)?.Trim() ?? string.Empty;
            var backupEmail = _configuration["Security:EmergencyAdmin:BackupSuperAdminEmail"]?.Trim() ?? string.Empty;
            var isBackupEmergencyAdmin = !string.IsNullOrWhiteSpace(currentUserEmail) &&
                                         !string.IsNullOrWhiteSpace(backupEmail) &&
                                         string.Equals(currentUserEmail, backupEmail, StringComparison.OrdinalIgnoreCase);

            ViewData["IsBackupEmergencyAdmin"] = isBackupEmergencyAdmin;
            return View();
        }

        // Company Admin - User Management
        [Authorize(Policy = "CompanyAdminOnly")]
        public IActionResult UserManagement()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Company Admin - Reports
        [Authorize(Policy = "CompanyAdminOnly")]
        public IActionResult CompanyAdminReports()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Company Admin Dashboard
        [Authorize(Policy = "CompanyAdminOnly")]
        public IActionResult Dashboard()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Project Manager Dashboard
        [Authorize(Policy = "ProjectManagerOnly")]
        public IActionResult ProjectManagerDashboard()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Team Member Dashboard
        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult TeamMemberDashboard()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Team Member - My Tasks
        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult MyTasks()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Projects - accessible by CompanyAdmin (read-only), ProjectManager (full), TeamMember (assigned only)
        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult Projects()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "ProjectManagerOnly")]
        public IActionResult Tasks()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "ProjectManagerOnly")]
        public IActionResult Reports()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult Documents()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult ProductLifecycle()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "CompanyAdminOnly")]
        public IActionResult Team()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult KnowledgeBase()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult Calendar()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult Activity()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "CompanyAdminOnly")]
        public IActionResult AuditLog()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "CompanyAdminOnly")]
        public IActionResult MySubscription()
        {
            SetUserRoleFromClaims();
            return View();
        }

        [Authorize(Policy = "TeamMemberOnly")]
        public IActionResult Settings()
        {
            SetUserRoleFromClaims();
            return View();
        }

        // Login page - accessible without authentication
        [AllowAnonymous]
        public IActionResult Login()
        {
            // Redirect to appropriate dashboard if already authenticated
            if (User.Identity?.IsAuthenticated == true)
            {
                var primaryRole = User.Claims.FirstOrDefault(c => c.Type == "PrimaryRole")?.Value ?? "TeamMember";
                return RedirectToAction(GetDashboardAction(primaryRole));
            }
            return View();
        }

        private string GetDashboardAction(string role)
        {
            return role.ToLower() switch
            {
                "superadmin" => "SuperAdminDashboard",
                "companyadmin" => "Dashboard",
                "projectmanager" => "ProjectManagerDashboard",
                "teammember" => "TeamMemberDashboard",
                _ => "Dashboard"
            };
        }
    }
}
