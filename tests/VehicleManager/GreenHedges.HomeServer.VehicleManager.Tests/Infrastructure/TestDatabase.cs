using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>
/// The tests' database: a throwaway database, created and migrated once per test run on the SQL Server named by
/// <c>ConnectionStrings:VehicleManagerTests</c> (the local container; see the README). It's never the app's own
/// database (<c>ConnectionStrings:VehicleManager</c>), and never Azure SQL. Each test still acts as its own throwaway
/// user (<c>test|…</c>) and deletes that user's data afterwards, so tests don't see each other's.
/// <para>The test host ends its process before a clean-up at the end of the run could finish, so each run instead drops
/// the test databases older than <see cref="StaleAfter"/>: at most the latest run's is left behind.</para>
/// </summary>
public static class TestDatabase
{
    public const string UserPrefix = "test|";
    public const string DatabasePrefix = "VehicleManager_Tests_";

    /// <summary>Test databases are dropped by a later run once they're this old (long enough to outlast a run in progress).</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private static readonly string? ServerConnectionString = new ConfigurationBuilder()
        .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build()
        .GetConnectionString("VehicleManagerTests");

    private static readonly Lazy<string> Database = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<DbContextOptions<AppDbContext>> Options = new(() => OptionsFor(ConnectionString));

    /// <summary>Whether a SQL Server for the tests is configured. Database tests are skipped without one.</summary>
    public static bool IsConfigured => ServerConnectionString is not null;

    /// <summary>The test database's connection string. Creates and migrates the database on first use.</summary>
    public static string ConnectionString => Database.Value;

    public static string NewUserId() => $"{UserPrefix}{Guid.NewGuid():N}";

    /// <summary>A context acting as <paramref name="userId"/> (null: no signed-in user).</summary>
    public static AppDbContext As(string? userId) => new(Options.Value, new TestCurrentUser(userId));

    /// <summary>Deletes everything a test user owns. Refuses anything but a test user.</summary>
    public static async Task DeleteUserDataAsync(string userId)
    {
        if (!userId.StartsWith(UserPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"Only test users ({UserPrefix}…) can be deleted.", nameof(userId));

        await using var db = As(null);
        await db.FuelEntryStops.IgnoreQueryFilters().Where(s => s.FuelEntry!.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.SentReminders.IgnoreQueryFilters().Where(r => r.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.MotDefects.IgnoreQueryFilters().Where(d => d.MotTest!.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.MotTests.IgnoreQueryFilters().Where(t => t.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.ServiceRecords.IgnoreQueryFilters().Where(s => s.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.FuelEntries.IgnoreQueryFilters().Where(f => f.Vehicle!.UserId == userId).ExecuteDeleteAsync();
        await db.Claims.IgnoreQueryFilters().Where(c => c.Owner!.UserId == userId).ExecuteDeleteAsync();
        await db.InsurancePolicyVehicles.IgnoreQueryFilters().Where(v => v.Policy!.Owner!.UserId == userId).ExecuteDeleteAsync();
        await db.InsurancePolicies.IgnoreQueryFilters().Where(p => p.Owner!.UserId == userId).ExecuteDeleteAsync();
        await db.Vehicles.IgnoreQueryFilters().Where(v => v.UserId == userId).ExecuteDeleteAsync();
        await db.Owners.IgnoreQueryFilters().Where(o => o.UserId == userId).ExecuteDeleteAsync();
        await db.FuelStations.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync();
        await db.FuelBrands.IgnoreQueryFilters().Where(b => b.UserId == userId).ExecuteDeleteAsync();
        await db.UserSettings.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync();
    }

    private static DbContextOptions<AppDbContext> OptionsFor(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

    /// <summary>Creates and migrates a new test database, and arranges for it to be dropped when the run ends.</summary>
    private static string Create()
    {
        if (ServerConnectionString is null)
            throw new InvalidOperationException("No SQL Server for the tests: set ConnectionStrings:VehicleManagerTests.");

        var server = new SqlConnectionStringBuilder(ServerConnectionString);
        if (server.DataSource.Contains(".database.windows.net", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ConnectionStrings:VehicleManagerTests points at Azure SQL. The tests only run against a local SQL Server.");

        server.InitialCatalog = "master";
        DropStaleDatabases(server.ConnectionString);

        var name = $"{DatabasePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..(DatabasePrefix.Length + 24)];
        var database = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };

        using (var db = new AppDbContext(OptionsFor(database.ConnectionString), new TestCurrentUser(null)))
            db.Database.Migrate(); // creates the database, then applies every migration

        return database.ConnectionString;
    }

    /// <summary>
    /// A new, empty test database of its own (not migrated), e.g. to apply migrations step by step to rows put in
    /// between. Dispose the result to drop it.
    /// </summary>
    public static ScratchDatabase NewScratchDatabase()
    {
        if (ServerConnectionString is null)
            throw new InvalidOperationException("No SQL Server for the tests: set ConnectionStrings:VehicleManagerTests.");
        var server = new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = "master" };
        var name = $"{DatabasePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..(DatabasePrefix.Length + 24)];
        var database = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
        return new ScratchDatabase(database.ConnectionString, () => Drop(server.ConnectionString, name));
    }

    public sealed class ScratchDatabase(string connectionString, Action drop) : IDisposable
    {
        public string ConnectionString => connectionString;
        public AppDbContext Context() => new(OptionsFor(connectionString), new TestCurrentUser(null));
        public void Dispose() => drop();
    }

    /// <summary>Drops test databases older than <see cref="StaleAfter"/>, left by earlier runs.</summary>
    private static void DropStaleDatabases(string masterConnectionString)
    {
        using var connection = new SqlConnection(masterConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sys.databases WHERE name LIKE @prefix + '%' AND create_date < DATEADD(MINUTE, -@minutes, GETDATE())";
        command.Parameters.AddWithValue("@prefix", DatabasePrefix.Replace("_", "[_]"));
        command.Parameters.AddWithValue("@minutes", (int)StaleAfter.TotalMinutes);
        var stale = new List<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) stale.Add(reader.GetString(0));

        foreach (var name in stale) Drop(masterConnectionString, name);
    }

    /// <summary>Drops a test database. Best effort: one that can't be dropped now is tried again by a later run.</summary>
    private static void Drop(string masterConnectionString, string name)
    {
        if (!name.StartsWith(DatabasePrefix, StringComparison.Ordinal))
            throw new ArgumentException($"Only test databases ({DatabasePrefix}…) can be dropped.", nameof(name));

        try
        {
            SqlConnection.ClearAllPools();
            using var connection = new SqlConnection(masterConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            var quoted = "[" + name.Replace("]", "]]") + "]";
            command.CommandText =
                $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END";
            command.Parameters.AddWithValue("@name", name);
            command.ExecuteNonQuery();
        }
        catch (SqlException)
        {
            // Tried again by the next run.
        }
    }
}
