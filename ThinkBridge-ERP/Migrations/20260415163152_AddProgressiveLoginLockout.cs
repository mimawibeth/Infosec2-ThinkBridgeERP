using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThinkBridge_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressiveLoginLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPermanentlyLocked",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LockoutLevel",
                table: "User",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PermanentlyLockedAt",
                table: "User",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "UserID",
                keyValue: -2,
                columns: new[] { "IsPermanentlyLocked", "LockoutLevel", "PermanentlyLockedAt" },
                values: new object[] { false, 0, null });

            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "UserID",
                keyValue: 1,
                columns: new[] { "IsPermanentlyLocked", "LockoutLevel", "PermanentlyLockedAt" },
                values: new object[] { false, 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPermanentlyLocked",
                table: "User");

            migrationBuilder.DropColumn(
                name: "LockoutLevel",
                table: "User");

            migrationBuilder.DropColumn(
                name: "PermanentlyLockedAt",
                table: "User");
        }
    }
}
