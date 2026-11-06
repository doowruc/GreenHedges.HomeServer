using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Data;

/// <summary>The InsuranceNamedDrivers migration, on rows from before it: typed-in names of owners become links.</summary>
public class NamedDriversMigrationTests
{
    [DatabaseFact]
    public async Task Typed_in_names_of_the_users_other_owners_become_named_driver_links()
    {
        using var scratch = TestDatabase.NewScratchDatabase();
        await using (var db = scratch.Context())
            await db.GetService<IMigrator>().MigrateAsync("OwnerClaims");   // the migration before

        await using (var connection = new SqlConnection(scratch.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                INSERT INTO Owners (UserId, Name, AddedDate, AddedBy, ModifiedDate, ModifiedBy) VALUES
                    ('u1', 'Wife', @now, 'u1', @now, 'u1'), ('u1', 'Me', @now, 'u1', @now, 'u1'), ('u2', 'Neighbour', @now, 'u2', @now, 'u2');
                INSERT INTO InsurancePolicies (OwnerId, StartDate, EndDate, Provider, PolicyNumber, Cover, Premium, NamedDrivers, ProtectedNoClaims,
                                               AddedDate, AddedBy, ModifiedDate, ModifiedBy) VALUES
                    ((SELECT Id FROM Owners WHERE Name = 'Wife'), '2026-01-01', '2026-12-31 23:59:00', 'Acme', 'P1', 'Comprehensive', 400, ' me , Grandad, Neighbour', 0, @now, 'u1', @now, 'u1'),
                    ((SELECT Id FROM Owners WHERE Name = 'Me'), '2026-01-01', '2026-12-31 23:59:00', 'Acme', 'P2', 'Comprehensive', 400, 'Me', 0, @now, 'u1', @now, 'u1'),
                    ((SELECT Id FROM Owners WHERE Name = 'Wife'), '2025-01-01', '2025-12-31 23:59:00', 'Acme', 'P3', 'Comprehensive', 400, NULL, 0, @now, 'u1', @now, 'u1');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = scratch.Context())
        {
            await db.Database.MigrateAsync();
            var drivers = await db.InsurancePolicyDrivers.IgnoreQueryFilters().Select(d => $"{d.Policy!.PolicyNumber}:{d.Owner!.Name}").ToListAsync();
            // "me" on the wife's policy (any case); not her neighbour (another user's owner), nor "Me" on my own policy.
            Assert.Equal(["P1:Me"], drivers);
            var text = await db.InsurancePolicies.IgnoreQueryFilters().OrderBy(p => p.PolicyNumber).Select(p => p.NamedDrivers).ToListAsync();
            Assert.Equal(["Grandad, Neighbour", "Me", null], text);
        }
    }
}
