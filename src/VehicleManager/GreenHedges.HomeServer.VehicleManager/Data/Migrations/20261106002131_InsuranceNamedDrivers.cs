using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InsuranceNamedDrivers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsurancePolicyDrivers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsurancePolicyId = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurancePolicyDrivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsurancePolicyDrivers_InsurancePolicies_InsurancePolicyId",
                        column: x => x.InsurancePolicyId,
                        principalTable: "InsurancePolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InsurancePolicyDrivers_Owners_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Owners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicyDrivers_InsurancePolicyId_OwnerId",
                table: "InsurancePolicyDrivers",
                columns: new[] { "InsurancePolicyId", "OwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicyDrivers_OwnerId",
                table: "InsurancePolicyDrivers",
                column: "OwnerId");

            // Named drivers typed in who are the policyholder's user's other owners become links, and leave the text
            // (matched on the comma-separated names, ignoring case and spaces at the ends, as owner names are unique).
            migrationBuilder.Sql("""
                INSERT INTO InsurancePolicyDrivers (InsurancePolicyId, OwnerId, AddedDate, AddedBy, ModifiedDate, ModifiedBy)
                SELECT DISTINCT p.Id, o.Id, SYSUTCDATETIME(), p.ModifiedBy, SYSUTCDATETIME(), p.ModifiedBy
                FROM InsurancePolicies p
                JOIN Owners holder ON holder.Id = p.OwnerId
                CROSS APPLY STRING_SPLIT(p.NamedDrivers, ',') name
                JOIN Owners o ON o.UserId = holder.UserId AND o.Id <> p.OwnerId AND o.Name = LTRIM(RTRIM(name.value))
                WHERE p.NamedDrivers IS NOT NULL;

                UPDATE p SET NamedDrivers = NULLIF((
                    SELECT STRING_AGG(LTRIM(RTRIM(name.value)), ', ')
                    FROM STRING_SPLIT(p.NamedDrivers, ',') name
                    WHERE LTRIM(RTRIM(name.value)) <> ''
                      AND NOT EXISTS (SELECT 1 FROM InsurancePolicyDrivers d JOIN Owners o ON o.Id = d.OwnerId
                                      WHERE d.InsurancePolicyId = p.Id AND o.Name = LTRIM(RTRIM(name.value)))), '')
                FROM InsurancePolicies p
                WHERE EXISTS (SELECT 1 FROM InsurancePolicyDrivers d WHERE d.InsurancePolicyId = p.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsurancePolicyDrivers");
        }
    }
}
