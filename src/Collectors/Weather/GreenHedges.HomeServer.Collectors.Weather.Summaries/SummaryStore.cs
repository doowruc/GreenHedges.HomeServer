using System.Data;
using Microsoft.Data.SqlClient;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries;

/// <summary>
/// The summaries' database on SQL Server, which Grafana reads: <c>MinuteSummaries</c>, and in <c>PublishState</c>
/// how far through the archive they are. Derived data only: drop the database (or empty the tables) and the
/// publisher rebuilds it all from the archive. The database itself is created by the host setup; this creates and
/// migrates the tables in it.
/// </summary>
public sealed class SummaryStore(string connectionString)
{
    // Append-only. Each entry upgrades the schema by one version (tracked in dbo.SchemaVersion), in its own transaction.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE dbo.MinuteSummaries (
            MinuteUtc       datetime2(0) NOT NULL CONSTRAINT PK_MinuteSummaries PRIMARY KEY,
            Messages        int          NOT NULL,
            TemperatureAvgC float        NULL,
            TemperatureMinC float        NULL,
            TemperatureMaxC float        NULL,
            HumidityAvg     float        NULL,
            WindAvgMs       float        NULL,
            GustMaxMs       float        NULL,
            WindDirDeg      float        NULL,
            RainMm          float        NULL,
            BatteryOk       bit          NULL,
            RssiAvgDb       float        NULL,
            SnrAvgDb        float        NULL
        );

        CREATE TABLE dbo.PublishState (
            Target          nvarchar(50) NOT NULL CONSTRAINT PK_PublishState PRIMARY KEY,
            LastMessageId   bigint       NOT NULL,
            UpdatedUtc      datetime2(0) NOT NULL
        );
        """,
    ];

    public static int SchemaVersion => Migrations.Length;

    /// <summary>Creates the tables if needed and applies any pending migrations.</summary>
    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(
            connection,
            null,
            "IF OBJECT_ID(N'dbo.SchemaVersion') IS NULL CREATE TABLE dbo.SchemaVersion (Version int NOT NULL); " +
            "IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion) INSERT dbo.SchemaVersion (Version) VALUES (0);",
            cancellationToken);

        int version;
        await using (var command = new SqlCommand("SELECT Version FROM dbo.SchemaVersion;", connection))
        {
            version = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        if (version > Migrations.Length)
        {
            throw new InvalidOperationException(
                $"Summary database schema version {version} is newer than this build supports ({Migrations.Length}).");
        }

        for (var next = version; next < Migrations.Length; next++)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, Migrations[next], cancellationToken);
            await ExecuteAsync(connection, transaction, $"UPDATE dbo.SchemaVersion SET Version = {next + 1};", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>The last archive message id published for <paramref name="target"/>; 0 if none yet.</summary>
    public async Task<long> GetPublishedIdAsync(string target, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT LastMessageId FROM dbo.PublishState WHERE Target = @target;", connection);
        command.Parameters.Add("@target", SqlDbType.NVarChar, 50).Value = target;
        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : 0;
    }

    /// <summary>
    /// Writes <paramref name="minutes"/> (replacing any already there) and moves <paramref name="target"/>'s
    /// watermark to <paramref name="lastMessageId"/>, in one transaction.
    /// </summary>
    public async Task WriteAsync(
        IReadOnlyList<MinuteSummary> minutes, string target, long lastMessageId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (minutes.Count > 0)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "SELECT TOP 0 * INTO #Minutes FROM dbo.MinuteSummaries;",
                cancellationToken);

            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "#Minutes" })
            {
                await bulk.WriteToServerAsync(ToTable(minutes), cancellationToken);
            }

            await ExecuteAsync(
                connection,
                transaction,
                """
                MERGE dbo.MinuteSummaries WITH (HOLDLOCK) AS target
                USING #Minutes AS source ON target.MinuteUtc = source.MinuteUtc
                WHEN MATCHED THEN UPDATE SET
                    Messages = source.Messages, TemperatureAvgC = source.TemperatureAvgC,
                    TemperatureMinC = source.TemperatureMinC, TemperatureMaxC = source.TemperatureMaxC,
                    HumidityAvg = source.HumidityAvg, WindAvgMs = source.WindAvgMs, GustMaxMs = source.GustMaxMs,
                    WindDirDeg = source.WindDirDeg, RainMm = source.RainMm, BatteryOk = source.BatteryOk,
                    RssiAvgDb = source.RssiAvgDb, SnrAvgDb = source.SnrAvgDb
                WHEN NOT MATCHED THEN INSERT (
                    MinuteUtc, Messages, TemperatureAvgC, TemperatureMinC, TemperatureMaxC, HumidityAvg, WindAvgMs,
                    GustMaxMs, WindDirDeg, RainMm, BatteryOk, RssiAvgDb, SnrAvgDb)
                VALUES (
                    source.MinuteUtc, source.Messages, source.TemperatureAvgC, source.TemperatureMinC,
                    source.TemperatureMaxC, source.HumidityAvg, source.WindAvgMs, source.GustMaxMs, source.WindDirDeg,
                    source.RainMm, source.BatteryOk, source.RssiAvgDb, source.SnrAvgDb);
                DROP TABLE #Minutes;
                """,
                cancellationToken);
        }

        await using (var command = new SqlCommand(
            """
            MERGE dbo.PublishState WITH (HOLDLOCK) AS target
            USING (SELECT @target AS Target) AS source ON target.Target = source.Target
            WHEN MATCHED THEN UPDATE SET LastMessageId = @id, UpdatedUtc = @now
            WHEN NOT MATCHED THEN INSERT (Target, LastMessageId, UpdatedUtc) VALUES (@target, @id, @now);
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@target", SqlDbType.NVarChar, 50).Value = target;
            command.Parameters.Add("@id", SqlDbType.BigInt).Value = lastMessageId;
            command.Parameters.Add("@now", SqlDbType.DateTime2).Value = now.UtcDateTime;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The minutes from <paramref name="fromUtc"/> (inclusive) to <paramref name="toUtc"/> (exclusive), in time order.</summary>
    public async Task<IReadOnlyList<MinuteSummary>> ReadMinutesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT MinuteUtc, Messages, TemperatureAvgC, TemperatureMinC, TemperatureMaxC, HumidityAvg, WindAvgMs,
                   GustMaxMs, WindDirDeg, RainMm, BatteryOk, RssiAvgDb, SnrAvgDb
            FROM dbo.MinuteSummaries WHERE MinuteUtc >= @from AND MinuteUtc < @to ORDER BY MinuteUtc;
            """,
            connection);
        command.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
        command.Parameters.Add("@to", SqlDbType.DateTime2).Value = toUtc;

        var minutes = new List<MinuteSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            minutes.Add(new MinuteSummary(
                DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                reader.GetInt32(1),
                Real(reader, 2), Real(reader, 3), Real(reader, 4), Real(reader, 5), Real(reader, 6), Real(reader, 7),
                Real(reader, 8), Real(reader, 9),
                reader.IsDBNull(10) ? null : reader.GetBoolean(10),
                Real(reader, 11), Real(reader, 12)));
        }

        return minutes;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DataTable ToTable(IReadOnlyList<MinuteSummary> minutes)
    {
        // Columns in the table's order, for SqlBulkCopy's default (ordinal) mapping.
        var table = new DataTable();
        table.Columns.Add("MinuteUtc", typeof(DateTime));
        table.Columns.Add("Messages", typeof(int));
        foreach (var name in new[] { "TemperatureAvgC", "TemperatureMinC", "TemperatureMaxC", "HumidityAvg", "WindAvgMs", "GustMaxMs", "WindDirDeg", "RainMm" })
        {
            table.Columns.Add(name, typeof(double));
        }

        table.Columns.Add("BatteryOk", typeof(bool));
        table.Columns.Add("RssiAvgDb", typeof(double));
        table.Columns.Add("SnrAvgDb", typeof(double));

        foreach (var m in minutes)
        {
            table.Rows.Add(
                m.MinuteUtc, m.Messages, Db(m.TemperatureAvgC), Db(m.TemperatureMinC), Db(m.TemperatureMaxC),
                Db(m.HumidityAvg), Db(m.WindAvgMs), Db(m.GustMaxMs), Db(m.WindDirDeg), Db(m.RainMm), Db(m.BatteryOk),
                Db(m.RssiAvgDb), Db(m.SnrAvgDb));
        }

        return table;
    }

    private static object Db<T>(T? value) where T : struct => value is { } v ? v : DBNull.Value;

    private static double? Real(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
