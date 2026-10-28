using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Common.Web.Backups;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Data;

public class SqlServerBackupSourceTests : DatabaseTest
{
    private static SqlServerBackupSource Source(string connectionString, SqlServerBackupOptions options) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:VehicleManager"] = connectionString
            }).Build(),
            Options.Create(options),
            new CollectorIdentity("VehicleManager"),
            NullLogger<SqlServerBackupSource>.Instance);

    [SqlServerBackupFact]
    public async Task The_backup_is_a_checksummed_copy_only_full_backup_of_the_apps_database()
    {
        await using (var db = Db())
            await TestData.AddVehicleAsync(db, "BAK 1");
        var folders = SqlServerBackupFolders.Local!;
        var source = Source(TestDatabase.ConnectionString, new SqlServerBackupOptions { SqlServerFolder = folders.Server, SqlServerFolderOnHost = folders.Host });
        var copy = Path.Combine(folders.Host, $"test-{Guid.NewGuid():N}.bak");
        try
        {
            await source.BackupToAsync(copy, CancellationToken.None);

            // Read it back as SQL Server sees it (the test login may; the app's own login needn't).
            await using var connection = new SqlConnection(TestDatabase.ConnectionString);
            await connection.OpenAsync();
            var serverPath = folders.Server + "/" + Path.GetFileName(copy);
            await using (var header = connection.CreateCommand())
            {
                header.CommandText = "RESTORE HEADERONLY FROM DISK = @path";
                header.Parameters.AddWithValue("@path", serverPath);
                await using var reader = await header.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(new SqlConnectionStringBuilder(TestDatabase.ConnectionString).InitialCatalog, reader["DatabaseName"]);
                Assert.Equal((byte)1, reader["BackupType"]); // full
                Assert.True((bool)reader["IsCopyOnly"]);
                Assert.Equal(1, Convert.ToInt32(reader["HasBackupChecksums"]));
            }

            await using var verify = connection.CreateCommand();
            verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM";
            verify.Parameters.AddWithValue("@path", serverPath);
            await verify.ExecuteNonQueryAsync(); // throws if the backup is damaged
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public async Task Without_a_folder_it_says_which_setting_is_missing()
    {
        var source = Source("Server=unused.invalid;Database=VehicleManager", new SqlServerBackupOptions());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.BackupToAsync(Path.GetTempFileName(), CancellationToken.None));
        Assert.Contains("Backup:SqlServerFolder", error.Message);
    }

    [SqlServerBackupFact]
    public async Task A_folder_the_app_cant_see_is_reported_rather_than_copying_nothing()
    {
        var folders = SqlServerBackupFolders.Local!;
        var source = Source(TestDatabase.ConnectionString, new SqlServerBackupOptions
        {
            SqlServerFolder = folders.Server,
            SqlServerFolderOnHost = Path.Combine(Path.GetTempPath(), $"not-shared-{Guid.NewGuid():N}")
        });

        var error = await Assert.ThrowsAsync<IOException>(() => source.BackupToAsync(Path.GetTempFileName(), CancellationToken.None));
        Assert.Contains("Backup:SqlServerFolderOnHost", error.Message);
    }
}
