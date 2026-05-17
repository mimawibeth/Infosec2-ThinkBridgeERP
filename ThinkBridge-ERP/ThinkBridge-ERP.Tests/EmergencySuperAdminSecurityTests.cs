using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services;
using ThinkBridge_ERP.Services.Interfaces;
using Xunit;

namespace ThinkBridge_ERP.Tests;

public class EmergencySuperAdminSecurityTests
{
    private static readonly string PrimaryEmail = $"primary.admin+{Guid.NewGuid():N}@example.com".ToLowerInvariant();
    private static readonly string BackupEmail = $"backup.admin+{Guid.NewGuid():N}@example.com".ToLowerInvariant();
    private static readonly string OtherEmail = $"other.admin+{Guid.NewGuid():N}@example.com".ToLowerInvariant();
    private static readonly string PrimaryPassword = $"PrimaryPass!{Guid.NewGuid():N}";
    private static readonly string BackupPassword = $"BackupPass!{Guid.NewGuid():N}";
    private static readonly string OtherPassword = $"OtherPass!{Guid.NewGuid():N}";

    [Fact]
    public async System.Threading.Tasks.Task BackupSuperAdmin_CanSuspendPrimary_AndWritesSecurityAudit()
    {
        await using var context = CreateContext();
        SeedSuperAdmins(context);

        var sut = CreateService(context);
        var result = await sut.SuspendPrimarySuperAdminAsync(
            performedByUserId: -2,
            performedByEmail: BackupEmail,
            request: new EmergencySuperAdminActionRequest
            {
                ConfirmationPhrase = "SUSPEND PRIMARY SUPERADMIN",
                Reason = "Primary account behavior indicates probable compromise.",
                CurrentPassword = BackupPassword
            },
            ipAddress: "127.0.0.1");

        Assert.True(result.Success);

        var primary = await context.Users.FirstAsync(u => u.UserID == 1);
        Assert.Equal("Suspended", primary.Status);
        Assert.True(primary.IsPermanentlyLocked);
        Assert.True(primary.MustChangePassword);

        var audit = await context.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action.StartsWith("EmergencySuspendPrimarySuperAdmin"));

        Assert.NotNull(audit);
        Assert.Equal("Security", audit!.LogType);
        Assert.Equal(1, audit.EntityID);
        Assert.Equal(-2, audit.UserID);
    }

    [Fact]
    public async System.Threading.Tasks.Task NonBackupSuperAdmin_CannotSuspendPrimary_AndGetsDeniedAudit()
    {
        await using var context = CreateContext();
        SeedSuperAdmins(context);

        var sut = CreateService(context);
        var result = await sut.SuspendPrimarySuperAdminAsync(
            performedByUserId: 99,
            performedByEmail: OtherEmail,
            request: new EmergencySuperAdminActionRequest
            {
                ConfirmationPhrase = "SUSPEND PRIMARY SUPERADMIN",
                Reason = "Unauthorized attempt simulation for testing.",
                CurrentPassword = OtherPassword
            },
            ipAddress: "127.0.0.1");

        Assert.False(result.Success);

        var primary = await context.Users.FirstAsync(u => u.UserID == 1);
        Assert.Equal("Active", primary.Status);

        var deniedAudit = await context.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action.StartsWith("EmergencySuspendPrimarySuperAdminDenied:CallerNotBackup"));

        Assert.NotNull(deniedAudit);
        Assert.Equal("Security", deniedAudit!.LogType);
        Assert.Equal(99, deniedAudit.UserID);
    }

    [Fact]
    public async System.Threading.Tasks.Task InvalidStepUpVerification_Fails_AndWritesDeniedAudit()
    {
        await using var context = CreateContext();
        SeedSuperAdmins(context);

        var sut = CreateService(context);
        var result = await sut.SuspendPrimarySuperAdminAsync(
            performedByUserId: -2,
            performedByEmail: BackupEmail,
            request: new EmergencySuperAdminActionRequest
            {
                ConfirmationPhrase = "SUSPEND PRIMARY SUPERADMIN",
                Reason = "Testing invalid step-up behavior.",
                CurrentPassword = $"WrongPass!{Guid.NewGuid():N}"
            },
            ipAddress: "127.0.0.1");

        Assert.False(result.Success);
        Assert.Contains("Step-up verification failed", result.ErrorMessage ?? string.Empty);

        var deniedAudit = await context.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action.StartsWith("EmergencySuspendPrimarySuperAdminDenied:StepUpFailed"));

        Assert.NotNull(deniedAudit);
        Assert.Equal("Security", deniedAudit!.LogType);
    }

    [Fact]
    public async System.Threading.Tasks.Task BackupSuperAdmin_CanReactivatePrimary_AndForcesPasswordChange()
    {
        await using var context = CreateContext();
        SeedSuperAdmins(context);

        var sut = CreateService(context);

        var suspendResult = await sut.SuspendPrimarySuperAdminAsync(
            performedByUserId: -2,
            performedByEmail: BackupEmail,
            request: new EmergencySuperAdminActionRequest
            {
                ConfirmationPhrase = "SUSPEND PRIMARY SUPERADMIN",
                Reason = "Temporary suspend before controlled restore.",
                CurrentPassword = BackupPassword
            },
            ipAddress: "127.0.0.1");

        Assert.True(suspendResult.Success);

        var reactivateResult = await sut.ReactivatePrimarySuperAdminAsync(
            performedByUserId: -2,
            performedByEmail: BackupEmail,
            request: new EmergencySuperAdminActionRequest
            {
                ConfirmationPhrase = "SUSPEND PRIMARY SUPERADMIN",
                Reason = "Compromise risk resolved, allowing controlled re-entry.",
                CurrentPassword = BackupPassword
            },
            ipAddress: "127.0.0.1");

        Assert.True(reactivateResult.Success);

        var primary = await context.Users.FirstAsync(u => u.UserID == 1);
        Assert.Equal("Active", primary.Status);
        Assert.False(primary.IsPermanentlyLocked);
        Assert.True(primary.MustChangePassword);

        Assert.True(await context.AuditLogs.AnyAsync(a => a.Action.StartsWith("EmergencyReactivatePrimarySuperAdmin")));
    }

    [Fact]
    public void SessionEvaluator_InvalidatesSuspendedPrimarySession()
    {
        var now = DateTime.UtcNow;

        var suspended = SessionSecurityEvaluator.IsAccountSessionValid("Suspended", isPermanentlyLocked: true, lockoutEndUtc: null, now);
        var active = SessionSecurityEvaluator.IsAccountSessionValid("Active", isPermanentlyLocked: false, lockoutEndUtc: null, now);

        Assert.False(suspended);
        Assert.True(active);
    }

    private static SuperAdminService CreateService(ApplicationDbContext context)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EmergencyAdmin:PrimarySuperAdminEmail"] = PrimaryEmail,
                ["Security:EmergencyAdmin:BackupSuperAdminEmail"] = BackupEmail,
                ["Security:EmergencyAdmin:ConfirmationPhrase"] = "SUSPEND PRIMARY SUPERADMIN",
                ["Security:EmergencyAdmin:CooldownSeconds"] = "0",
                ["Security:Totp:Digits"] = "6",
                ["Security:Totp:PeriodSeconds"] = "30",
                ["Security:Totp:AllowedDriftWindows"] = "1"
            })
            .Build();

        return new SuperAdminService(context, NullLogger<SuperAdminService>.Instance, config);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tb-emergency-security-{Guid.NewGuid()}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedSuperAdmins(ApplicationDbContext context)
    {
        context.Users.Add(new User
        {
            UserID = 1,
            Fname = "Super",
            Lname = "Admin",
            Email = PrimaryEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(PrimaryPassword),
            IsSuperAdmin = true,
            Status = "Active",
            MustChangePassword = false,
            CreatedAt = DateTime.UtcNow
        });

        context.Users.Add(new User
        {
            UserID = -2,
            Fname = "Backup",
            Lname = "Admin",
            Email = BackupEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(BackupPassword),
            IsSuperAdmin = true,
            Status = "Active",
            MustChangePassword = false,
            CreatedAt = DateTime.UtcNow
        });

        context.Users.Add(new User
        {
            UserID = 99,
            Fname = "Other",
            Lname = "Admin",
            Email = OtherEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(OtherPassword),
            IsSuperAdmin = true,
            Status = "Active",
            MustChangePassword = false,
            CreatedAt = DateTime.UtcNow
        });

        context.SaveChanges();
    }
}
