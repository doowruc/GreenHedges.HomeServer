using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Common.Web.Backups;

namespace GreenHedges.HomeServer.Common.Web.Tests;

/// <summary>What each app's backup is called. Vehicle Manager's tests take real backups on SQL Server.</summary>
public class SqlServerBackupSourceTests
{
    private static SqlServerBackupSource Source(string app, Dictionary<string, string?> settings) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            Options.Create(new SqlServerBackupOptions { SqlServerFolder = "/var/opt/mssql/backups" }),
            new CollectorIdentity(app),
            NullLogger<SqlServerBackupSource>.Instance);

    [Fact]
    public void The_shared_folders_file_is_named_after_the_app() =>
        Assert.Equal("payandpensions-latest.bak", Source("PayAndPensions", []).FileName);

    [Fact]
    public async Task The_database_is_the_one_in_the_apps_connection_string()
    {
        var source = Source("PayAndPensions", new() { ["ConnectionStrings:VehicleManager"] = "Server=unused.invalid" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.BackupToAsync(Path.GetTempFileName(), CancellationToken.None));
        Assert.Contains("ConnectionStrings:PayAndPensions", error.Message);
    }
}
