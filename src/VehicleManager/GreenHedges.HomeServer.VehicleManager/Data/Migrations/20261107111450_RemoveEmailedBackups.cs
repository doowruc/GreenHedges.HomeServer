using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEmailedBackups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupEmail",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "LastBackupDate",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "LastBackupHash",
                table: "UserSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupEmail",
                table: "UserSettings",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastBackupDate",
                table: "UserSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastBackupHash",
                table: "UserSettings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
