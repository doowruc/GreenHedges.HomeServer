using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.PayAndPensions.Data.Migrations
{
    /// <inheritdoc />
    public partial class PensionTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PensionTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PensionId = table.Column<int>(type: "int", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SettleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Fund = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    Value = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PensionTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PensionTransactions_Pensions_PensionId",
                        column: x => x.PensionId,
                        principalTable: "Pensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PensionTransactions_PensionId_TradeDate",
                table: "PensionTransactions",
                columns: new[] { "PensionId", "TradeDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PensionTransactions");
        }
    }
}
