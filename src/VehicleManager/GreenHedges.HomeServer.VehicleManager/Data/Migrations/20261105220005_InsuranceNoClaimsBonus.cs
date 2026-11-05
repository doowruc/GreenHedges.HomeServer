using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InsuranceNoClaimsBonus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NoClaimsYears",
                table: "InsurancePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProtectedNoClaims",
                table: "InsurancePolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoClaimsYears",
                table: "InsurancePolicies");

            migrationBuilder.DropColumn(
                name: "ProtectedNoClaims",
                table: "InsurancePolicies");
        }
    }
}
