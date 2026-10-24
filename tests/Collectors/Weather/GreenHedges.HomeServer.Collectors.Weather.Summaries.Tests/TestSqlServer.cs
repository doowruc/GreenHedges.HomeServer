using Microsoft.Data.SqlClient;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries.Tests;

/// <summary>
/// A throwaway database per test on the local SQL Server: the one CI provides
/// (<c>ConnectionStrings__VehicleManagerTests</c>, or <c>ConnectionStrings__SqlServerTests</c>), else the repo's
/// <c>compose.yaml</c> container, using the sa password in the repo root's <c>.env</c>. Each is dropped afterwards;
/// any a crashed run left behind are dropped by a later run once an hour old.
/// </summary>
public sealed class TestSqlServer : IAsyncDisposable
{
    public const string DatabasePrefix = "Weather_Tests_";

    private static readonly string? ServerConnectionString = FindServer();
    private static readonly Lazy<Task> StaleDropped = new(() => DropStaleAsync());

    private readonly string _name;

    private TestSqlServer(string name, string connectionString)
    {
        _name = name;
        ConnectionString = connectionString;
    }

    /// <summary>Whether a SQL Server for the tests was found. SQL Server tests are skipped without one.</summary>
    public static bool IsConfigured => ServerConnectionString is not null;

    public string ConnectionString { get; }

    /// <summary>Creates a new, empty database.</summary>
    public static async Task<TestSqlServer> CreateAsync()
    {
        await StaleDropped.Value;
        var name = $"{DatabasePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        await ExecuteOnMasterAsync($"CREATE DATABASE [{name}];");
        var database = new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = name };
        return new TestSqlServer(name, database.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await DropAsync(_name);
    }

    private static string? FindServer()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTests")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__VehicleManagerTests");
        if (configured is not null)
        {
            return configured;
        }

        // The local container (compose.yaml), whose sa password is in .env at the repo root.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var env = Path.Combine(dir.FullName, ".env");
            if (File.Exists(Path.Combine(dir.FullName, "compose.yaml")) && File.Exists(env))
            {
                var password = File.ReadAllLines(env)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("MSSQL_SA_PASSWORD=", StringComparison.Ordinal))
                    .Select(line => line["MSSQL_SA_PASSWORD=".Length..])
                    .LastOrDefault();
                return string.IsNullOrEmpty(password)
                    ? null
                    : new SqlConnectionStringBuilder
                    {
                        DataSource = "localhost,1433",
                        UserID = "sa",
                        Password = password,
                        TrustServerCertificate = true,
                    }.ConnectionString;
            }
        }

        return null;
    }

    private static async Task DropStaleAsync()
    {
        await using var connection = await OpenMasterAsync();
        await using var command = new SqlCommand(
            "SELECT name FROM sys.databases WHERE name LIKE @prefix + '%' AND create_date < DATEADD(HOUR, -1, GETDATE());",
            connection);
        command.Parameters.AddWithValue("@prefix", DatabasePrefix.Replace("_", "[_]"));
        var stale = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                stale.Add(reader.GetString(0));
            }
        }

        foreach (var name in stale)
        {
            await DropAsync(name);
        }
    }

    private static async Task DropAsync(string name)
    {
        if (!name.StartsWith(DatabasePrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Only test databases ({DatabasePrefix}…) can be dropped.", nameof(name));
        }

        try
        {
            await ExecuteOnMasterAsync(
                $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END");
        }
        catch (SqlException)
        {
            // Dropped by a later run instead.
        }
    }

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = await OpenMasterAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SqlConnection> OpenMasterAsync()
    {
        var master = new SqlConnectionStringBuilder(ServerConnectionString ?? throw new InvalidOperationException("No SQL Server for the tests."))
        {
            InitialCatalog = "master",
        };
        var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

/// <summary>A test that needs SQL Server; skipped when none is available (see <see cref="TestSqlServer"/>).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!TestSqlServer.IsConfigured)
        {
            Skip = "No SQL Server for the tests: start compose.yaml (with .env), or set ConnectionStrings__SqlServerTests.";
        }
    }
}
