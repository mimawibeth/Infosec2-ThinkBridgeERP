using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThinkBridge_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOtpMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<int>(
                name: "OtpFailedAttempts",
                table: "User",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpLastSentAt",
                table: "User",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "UserID",
                keyValue: 1,
                columns: new[] { "MfaEnabled", "OtpCodeHash", "OtpExpiresAt", "OtpLastSentAt" },
                values: new object[] { true, null, null, null });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "OtpFailedAttempts",
                table: "User");

            migrationBuilder.DropColumn(
                name: "OtpLastSentAt",
                table: "User");
        }
    }
}
