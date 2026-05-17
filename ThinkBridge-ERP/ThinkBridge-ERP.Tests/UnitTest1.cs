using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Services;
using Xunit;
using UserEntity = ThinkBridge_ERP.Models.Entities.User;

namespace ThinkBridge_ERP.Tests;

public class AuthServiceTests
{
    private static readonly string BackupEmail = $"backup.admin+{Guid.NewGuid():N}@example.com".ToLowerInvariant();
    private static readonly string BackupPassword = $"BackupPass!{Guid.NewGuid():N}";
    private static readonly string PrimaryEmail = $"primary.admin+{Guid.NewGuid():N}@example.com".ToLowerInvariant();
    private static readonly string PrimaryPassword = $"PrimaryPass!{Guid.NewGuid():N}";

    [Fact]
    public async System.Threading.Tasks.Task AuthenticateAsync_AllowsBackupAdminLogin_WithMixedCaseAndWhitespaceEmail()
    {
        await using var context = CreateContext();
        var backupUser = context.Users.Single(u => u.Email == BackupEmail);

        backupUser.Password = BCrypt.Net.BCrypt.HashPassword(BackupPassword);
        backupUser.Status = "Active";
        backupUser.IsSuperAdmin = true;
        backupUser.FailedLoginAttempts = 0;
        backupUser.LockoutEnd = null;
        backupUser.LockoutLevel = 0;
        backupUser.IsPermanentlyLocked = false;

        await context.SaveChangesAsync();

        var sut = new AuthService(context, NullLogger<AuthService>.Instance);
    var result = await sut.AuthenticateAsync($"  {BackupEmail.ToUpperInvariant()}  ", BackupPassword);

        Assert.True(result.Success);
        Assert.NotNull(result.User);
        Assert.Equal(BackupEmail, result.User!.Email);
    }

    [Fact]
    public async System.Threading.Tasks.Task AuthenticateAsync_ReturnsInvalidCredentials_ForWrongPassword()
    {
        await using var context = CreateContext();
        var backupUser = context.Users.Single(u => u.Email == BackupEmail);

        backupUser.Password = BCrypt.Net.BCrypt.HashPassword(BackupPassword);
        backupUser.Status = "Active";
        backupUser.IsSuperAdmin = true;
        backupUser.FailedLoginAttempts = 0;
        backupUser.LockoutEnd = null;
        backupUser.LockoutLevel = 0;
        backupUser.IsPermanentlyLocked = false;

        await context.SaveChangesAsync();

        var sut = new AuthService(context, NullLogger<AuthService>.Instance);
    var result = await sut.AuthenticateAsync(BackupEmail, $"WrongPass!{Guid.NewGuid():N}");

        Assert.False(result.Success);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task AuthenticateAsync_BlocksSuspendedPrimarySuperAdmin()
    {
        await using var context = CreateContext();
        var primaryUser = context.Users.SingleOrDefault(u => u.UserID == 1)
            ?? new UserEntity
        {
            UserID = 1,
            Fname = "Super",
            Lname = "Admin",
            Email = PrimaryEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(PrimaryPassword),
            IsSuperAdmin = true,
            Status = "Suspended",
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };

        if (context.Entry(primaryUser).State == EntityState.Detached)
        {
            context.Users.Add(primaryUser);
        }
        else
        {
            primaryUser.Password = BCrypt.Net.BCrypt.HashPassword(PrimaryPassword);
            primaryUser.Status = "Suspended";
            primaryUser.MustChangePassword = true;
            primaryUser.IsSuperAdmin = true;
        }

        await context.SaveChangesAsync();

        var sut = new AuthService(context, NullLogger<AuthService>.Instance);
    var result = await sut.AuthenticateAsync(PrimaryEmail, PrimaryPassword);

        Assert.False(result.Success);
        Assert.Equal("account_inactive", result.ErrorCode);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tb-auth-tests-{Guid.NewGuid()}")
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();

        if (!context.Users.Any(u => u.Email == BackupEmail))
        {
            context.Users.Add(new UserEntity
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

            context.SaveChanges();
        }

        return context;
    }
}
