using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.PayAndPensions.Data.Migrations
{
    /// <inheritdoc />
    public partial class PayslipPension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PensionId",
                table: "Payslips",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_PensionId",
                table: "Payslips",
                column: "PensionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payslips_Pensions_PensionId",
                table: "Payslips",
                column: "PensionId",
                principalTable: "Pensions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payslips_Pensions_PensionId",
                table: "Payslips");

            migrationBuilder.DropIndex(
                name: "IX_Payslips_PensionId",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "PensionId",
                table: "Payslips");
        }
    }
}
