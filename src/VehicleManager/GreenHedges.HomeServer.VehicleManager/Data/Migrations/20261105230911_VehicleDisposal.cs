using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class VehicleDisposal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisposalMethod",
                table: "Vehicles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DisposalPrice",
                table: "Vehicles",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DisposedDate",
                table: "Vehicles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePrice",
                table: "Vehicles",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisposalMethod",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DisposalPrice",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DisposedDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PurchasePrice",
                table: "Vehicles");
        }
    }
}
