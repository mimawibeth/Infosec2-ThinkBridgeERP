using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Data;

namespace ThinkBridge_ERP.Services;

/// <summary>
/// Keeps company user Status in sync when company or subscription access changes.
/// Login checks User.Status (not Company.Status).
/// </summary>
public static class CompanyUserStatusSync
{
    public static async Task ApplyForCompanyStatusAsync(ApplicationDbContext context, int companyId, string companyStatus)
    {
        var userStatus = MapCompanyStatusToUserStatus(companyStatus);
        if (userStatus == null)
            return;

        var users = await context.Users
            .Where(u => u.CompanyID == companyId && !u.IsSuperAdmin)
            .ToListAsync();

        foreach (var user in users)
            user.Status = userStatus;
    }

    public static string? MapCompanyStatusToUserStatus(string companyStatus)
    {
        var normalized = companyStatus.Trim();
        if (normalized.Equals("Active", StringComparison.OrdinalIgnoreCase))
            return "Active";

        if (normalized.Equals("Inactive", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            return "Inactive";

        return null;
    }
}
