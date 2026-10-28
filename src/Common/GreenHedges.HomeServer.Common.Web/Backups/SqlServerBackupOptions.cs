namespace GreenHedges.HomeServer.Common.Web.Backups;

/// <summary>
/// Where SQL Server writes the nightly backup, from the shared <c>Backup</c> section (with <c>Common</c>'s
/// <c>BackupOptions</c>). SQL Server runs in a container, so the same folder has two paths.
/// </summary>
public sealed class SqlServerBackupOptions
{
    /// <summary>The folder as SQL Server sees it, e.g. <c>/var/opt/mssql/backups</c> inside its container.</summary>
    public string? SqlServerFolder { get; set; }

    /// <summary>The same folder as this app sees it, e.g. <c>/var/lib/sqlserver/backups</c>. Defaults to <see cref="SqlServerFolder"/>.</summary>
    public string? SqlServerFolderOnHost { get; set; }

    public string? FolderOnHost => string.IsNullOrWhiteSpace(SqlServerFolderOnHost) ? SqlServerFolder : SqlServerFolderOnHost;
}
