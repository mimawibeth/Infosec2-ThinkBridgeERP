using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThinkBridge_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddTotpMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "User");

            migrationBuilder.DropColumn(
                name: "OtpCodeHash",
                table: "User");

            migrationBuilder.DropColumn(
                name: "OtpExpiresAt",
                table: "User");

            migrationBuilder.RenameColumn(
                name: "OtpLastSentAt",
                table: "User",
                newName: "TotpLockoutUntil");

            migrationBuilder.RenameColumn(
                name: "OtpFailedAttempts",
                table: "User",
                newName: "TotpFailedAttempts");

            migrationBuilder.AddColumn<bool>(
                name: "IsTotpEnabled",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TotpBackupCodes",
                table: "User",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecret",
                table: "User",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "UserID",
                keyValue: -2,
                columns: new[] { "TotpBackupCodes", "TotpSecret" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "UserID",
                keyValue: 1,
                columns: new[] { "TotpBackupCodes", "TotpSecret" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTotpEnabled",
                table: "User");

            migrationBuilder.DropColumn(
                name: "TotpBackupCodes",
                table: "User");

            migrationBuilder.DropColumn(
                name: "TotpSecret",
                table: "User");

            migrationBuilder.RenameColumn(
                name: "TotpLockoutUntil",
                table: "User",
                newName: "OtpLastSentAt");

            migrationBuilder.RenameColumn(
                name: "TotpFailedAttempts",
                table: "User",
                newName: "OtpFailedAttempts");

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "OtpCodeHash",
                table: "User",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpExpiresAt",
                table: "User",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "UserID",
                keyValue: -2,
                columns: new[] { "MfaEnabled", "OtpCodeHash", "OtpExpiresAt" },
                values: new object[] { true, null, null });

            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "UserID",
                keyValue: 1,
                columns: new[] { "MfaEnabled", "OtpCodeHash", "OtpExpiresAt" },
                values: new object[] { true, null, null });
        }
    }
}
