using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Hosting;

namespace GreenHedges.HomeServer.Common.Web.Backups;

/// <summary>
/// The nightly backup for <c>Common</c>'s <c>BackupService</c>: SQL Server writes a full backup of the app's database
/// (<c>BACKUP DATABASE … WITH CHECKSUM, COPY_ONLY</c>, so every page's checksum is checked as it's read, and the
/// database's own backup chain is untouched) into a folder it shares with this app, which copies it out. The database
/// is the one in connection string <c>ConnectionStrings:&lt;name&gt;</c>, the name being the app's
/// <see cref="CollectorIdentity"/> (e.g. <c>VehicleManager</c>).
/// <c>BackupService</c> then gzips it, keeps 7, and the off-site copy and the Mac's pull follow.
/// </summary>
/// <remarks>
/// The shared folder holds one file, overwritten each time, so nothing piles up there even if this app can't delete
/// files SQL Server made. Checking a backup with <c>RESTORE VERIFYONLY</c> needs a login that can create databases, which
/// the app's own login shouldn't have; the tests and the restore test do that instead.
/// </remarks>
public sealed class SqlServerBackupSource(
    IConfiguration configuration,
    IOptions<SqlServerBackupOptions> options,
    CollectorIdentity identity,
    ILogger<SqlServerBackupSource> logger) : IBackupSource
{
    /// <summary>The file in the shared folder, overwritten by each backup, e.g. <c>vehiclemanager-latest.bak</c>.</summary>
    public string FileName { get; } = $"{identity.Name.ToLowerInvariant()}-latest.bak";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    private readonly SqlServerBackupOptions _options = options.Value;

    public string FileExtension => ".bak";

    public async Task BackupToAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var serverFolder = _options.SqlServerFolder is { Length: > 0 } folder
            ? folder
            : throw new InvalidOperationException("Backup:SqlServerFolder is not set: where SQL Server writes the backup.");
        // SQL Server runs on Linux (the container), so its path takes forward slashes whatever this machine uses.
        var serverPath = serverFolder.TrimEnd('/') + "/" + FileName;
        var hostPath = Path.Combine(_options.FolderOnHost!, FileName);

        var connectionString = configuration.GetConnectionString(identity.Name)
            ?? throw new InvalidOperationException($"ConnectionStrings:{identity.Name} is not set.");
        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
            command.CommandText =
                "BACKUP DATABASE @database TO DISK = @path WITH CHECKSUM, COPY_ONLY, INIT, FORMAT, NAME = @name";
            command.Parameters.AddWithValue("@database", database);
            command.Parameters.AddWithValue("@path", serverPath);
            command.Parameters.AddWithValue("@name", $"{identity.Name} nightly backup {DateTime.UtcNow:u}");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!File.Exists(hostPath))
            throw new IOException(
                $"SQL Server wrote the backup to {serverPath}, but it isn't at {hostPath}: check Backup:SqlServerFolderOnHost and the shared folder.");

        File.Copy(hostPath, destinationPath, overwrite: true);
        logger.LogDebug("Copied {Database}'s backup from {Path}", database, hostPath);
    }
}
