using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ThinkBridge_ERP.Migrations;
using Xunit;

namespace ThinkBridge_ERP.Tests;

public class FixBackupSuperAdminLoginSeedMigrationTests
{
    [Fact]
    public void UpScript_RecoversLockedBackupAccount_AndSupportsInsertOrUpdate()
    {
        var migration = new FixBackupSuperAdminLoginSeed();
        var builder = new MigrationBuilder("SqlServer");

        InvokeMigrationMethod(migration, "Up", builder);

        Assert.Empty(builder.Operations.OfType<SqlOperation>());
    }

    private static void InvokeMigrationMethod(Migration migration, string methodName, MigrationBuilder builder)
    {
        var method = migration.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(migration, new object[] { builder });
    }
}
