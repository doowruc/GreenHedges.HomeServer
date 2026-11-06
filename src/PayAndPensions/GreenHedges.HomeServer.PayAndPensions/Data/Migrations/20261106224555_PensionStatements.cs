using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.PayAndPensions.Data.Migrations
{
    /// <inheritdoc />
    public partial class PensionStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "Pensions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StatementTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PensionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HasProjections = table.Column<bool>(type: "bit", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatementTypes_Pensions_PensionId",
                        column: x => x.PensionId,
                        principalTable: "Pensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PensionStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementTypeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ProjectedFund = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: true),
                    ProjectedAnnualIncome = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PensionStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PensionStatements_StatementTypes_StatementTypeId",
                        column: x => x.StatementTypeId,
                        principalTable: "StatementTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PensionStatements_StatementTypeId_Date",
                table: "PensionStatements",
                columns: new[] { "StatementTypeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementTypes_PensionId_Name",
                table: "StatementTypes",
                columns: new[] { "PensionId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PensionStatements");

            migrationBuilder.DropTable(
                name: "StatementTypes");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Pensions");
        }
    }
}
