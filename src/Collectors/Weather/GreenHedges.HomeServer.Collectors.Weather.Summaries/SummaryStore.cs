using System.Data;
using Microsoft.Data.SqlClient;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries;

/// <summary>
/// The summaries' database on SQL Server, which Grafana reads: <c>MinuteSummaries</c> from the radio,
/// <c>HistoryObservations</c> from the station's uploads (Weather Underground, Weathercloud), and in <c>PublishState</c>
/// how far through the archive each is. Derived data only: drop the database (or empty the tables) and the
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
        // v2: the station's own history, as uploaded to Weather Underground and Weathercloud, in one shape. Each row is
        // an interval ending at TimeUtc. Pressure and the indoor readings come only from here (the radio doesn't carry them).
        """
        CREATE TABLE dbo.HistoryObservations (
            Source             varchar(20)  NOT NULL,
            TimeUtc            datetime2(0) NOT NULL,
            IntervalMinutes    int          NOT NULL,
            TemperatureAvgC    float        NULL,
            TemperatureMinC    float        NULL,
            TemperatureMaxC    float        NULL,
            HumidityAvg        float        NULL,
            DewPointC          float        NULL,
            WindAvgMs          float        NULL,
            GustMaxMs          float        NULL,
            WindDirDeg         float        NULL,
            RainMm             float        NULL,
            PressureHpa        float        NULL,
            IndoorTemperatureC float        NULL,
            IndoorHumidity     float        NULL,
            CONSTRAINT PK_HistoryObservations PRIMARY KEY (Source, TimeUtc)
        );
        CREATE INDEX IX_HistoryObservations_Time ON dbo.HistoryObservations (TimeUtc) INCLUDE (Source);
        """,
        // v3: what the dashboards read, merging the sources. Outdoor: for each UTC hour, the radio's minutes if the radio
        // has any in that hour, else Weather Underground's, else Weathercloud's (so a long range shows everything since
        // 2021, each hour from its best source). Pressure: Weather Underground, else Weathercloud. Indoor: Weathercloud.
        // History rows are intervals ending at TimeUtc; the radio's dew point is calculated here (Magnus formula).
        """
        CREATE VIEW dbo.Outdoor AS
        SELECT MinuteUtc AS TimeUtc, CAST('radio' AS varchar(20)) AS Source, 1 AS IntervalMinutes,
               TemperatureAvgC, TemperatureMinC, TemperatureMaxC, HumidityAvg,
               CASE WHEN HumidityAvg > 0 AND TemperatureAvgC IS NOT NULL
                    THEN 243.12 * (LOG(HumidityAvg / 100.0) + 17.62 * TemperatureAvgC / (243.12 + TemperatureAvgC))
                         / (17.62 - (LOG(HumidityAvg / 100.0) + 17.62 * TemperatureAvgC / (243.12 + TemperatureAvgC))) END AS DewPointC,
               WindAvgMs, GustMaxMs, WindDirDeg, RainMm
        FROM dbo.MinuteSummaries
        UNION ALL
        SELECT h.TimeUtc, h.Source, h.IntervalMinutes,
               h.TemperatureAvgC, COALESCE(h.TemperatureMinC, h.TemperatureAvgC), COALESCE(h.TemperatureMaxC, h.TemperatureAvgC),
               h.HumidityAvg, h.DewPointC, h.WindAvgMs, h.GustMaxMs, h.WindDirDeg, h.RainMm
        FROM dbo.HistoryObservations h
        WHERE NOT EXISTS (
                  SELECT 1 FROM dbo.MinuteSummaries m
                  WHERE m.MinuteUtc >= DATEADD(HOUR, DATEDIFF(HOUR, 0, h.TimeUtc), 0)
                    AND m.MinuteUtc < DATEADD(HOUR, DATEDIFF(HOUR, 0, h.TimeUtc) + 1, 0))
          AND (h.Source = 'wunderground' OR NOT EXISTS (
                  SELECT 1 FROM dbo.HistoryObservations w
                  WHERE w.Source = 'wunderground'
                    AND w.TimeUtc >= DATEADD(HOUR, DATEDIFF(HOUR, 0, h.TimeUtc), 0)
                    AND w.TimeUtc < DATEADD(HOUR, DATEDIFF(HOUR, 0, h.TimeUtc) + 1, 0)));
        GO
        CREATE VIEW dbo.Pressure AS
        SELECT h.TimeUtc, h.Source, h.PressureHpa
        FROM dbo.HistoryObservations h
        WHERE h.PressureHpa IS NOT NULL
          AND (h.Source = 'wunderground' OR NOT EXISTS (
                  SELECT 1 FROM dbo.HistoryObservations w
                  WHERE w.Source = 'wunderground' AND w.PressureHpa IS NOT NULL
                    AND w.TimeUtc >= DATEADD(HOUR, DATEDIFF(HOUR, 0, h.TimeUtc), 0)
                    AND w.TimeUtc < DATEADD(HOUR, DATEDIFF(HOUR, 0, h.TimeUtc) + 1, 0)));
        GO
        CREATE VIEW dbo.Indoor AS
        SELECT TimeUtc, IndoorTemperatureC AS TemperatureC, IndoorHumidity AS Humidity
        FROM dbo.HistoryObservations
        WHERE Source = 'weathercloud' AND (IndoorTemperatureC IS NOT NULL OR IndoorHumidity IS NOT NULL);
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
            // A migration may hold several batches (CREATE VIEW must start one), separated by GO lines.
            foreach (var batch in Migrations[next].Split("\nGO\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                await ExecuteAsync(connection, transaction, batch, cancellationToken);
            }

            await ExecuteAsync(connection, transaction, $"UPDATE dbo.SchemaVersion SET Version = {next + 1};", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// How far <paramref name="target"/> has published (its watermark, stored in <c>PublishState.LastMessageId</c>: an
    /// archive message id for the summaries, a fetch time in Unix milliseconds for history); 0 if nothing yet.
    /// </summary>
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

        await SetPositionAsync(connection, transaction, target, lastMessageId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SetPositionAsync(
        SqlConnection connection, SqlTransaction transaction, string target, long position, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            MERGE dbo.PublishState WITH (HOLDLOCK) AS target
            USING (SELECT @target AS Target) AS source ON target.Target = source.Target
            WHEN MATCHED THEN UPDATE SET LastMessageId = @id, UpdatedUtc = @now
            WHEN NOT MATCHED THEN INSERT (Target, LastMessageId, UpdatedUtc) VALUES (@target, @id, @now);
            """,
            connection,
            transaction);
        command.Parameters.Add("@target", SqlDbType.NVarChar, 50).Value = target;
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = position;
        command.Parameters.Add("@now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces <paramref name="source"/>'s observations from <paramref name="fromUtc"/> (inclusive) to
    /// <paramref name="toUtc"/> (exclusive) with <paramref name="observations"/>, and moves <paramref name="target"/>'s
    /// watermark to <paramref name="position"/>, in one transaction.
    /// </summary>
    public async Task ReplaceHistoryAsync(
        string source, DateTime fromUtc, DateTime toUtc, IReadOnlyList<HistoryObservation> observations,
        string target, long position, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = new SqlCommand(
            "DELETE dbo.HistoryObservations WHERE Source = @source AND TimeUtc >= @from AND TimeUtc < @to;", connection, transaction))
        {
            delete.Parameters.Add("@source", SqlDbType.VarChar, 20).Value = source;
            delete.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
            delete.Parameters.Add("@to", SqlDbType.DateTime2).Value = toUtc;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (observations.Count > 0)
        {
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "dbo.HistoryObservations" };
            await bulk.WriteToServerAsync(ToTable(source, observations), cancellationToken);
        }

        await SetPositionAsync(connection, transaction, target, position, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary><paramref name="source"/>'s observations from <paramref name="fromUtc"/> (inclusive) to <paramref name="toUtc"/> (exclusive).</summary>
    public async Task<IReadOnlyList<HistoryObservation>> ReadHistoryAsync(
        string source, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT TimeUtc, IntervalMinutes, TemperatureAvgC, TemperatureMinC, TemperatureMaxC, HumidityAvg, DewPointC,
                   WindAvgMs, GustMaxMs, WindDirDeg, RainMm, PressureHpa, IndoorTemperatureC, IndoorHumidity
            FROM dbo.HistoryObservations WHERE Source = @source AND TimeUtc >= @from AND TimeUtc < @to ORDER BY TimeUtc;
            """,
            connection);
        command.Parameters.Add("@source", SqlDbType.VarChar, 20).Value = source;
        command.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
        command.Parameters.Add("@to", SqlDbType.DateTime2).Value = toUtc;

        var rows = new List<HistoryObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new HistoryObservation(
                DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc), reader.GetInt32(1),
                Real(reader, 2), Real(reader, 3), Real(reader, 4), Real(reader, 5), Real(reader, 6), Real(reader, 7),
                Real(reader, 8), Real(reader, 9), Real(reader, 10), Real(reader, 11), Real(reader, 12), Real(reader, 13)));
        }

        return rows;
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

    private static DataTable ToTable(string source, IReadOnlyList<HistoryObservation> rows)
    {
        // Columns in the table's order, for SqlBulkCopy's default (ordinal) mapping.
        var table = new DataTable();
        table.Columns.Add("Source", typeof(string));
        table.Columns.Add("TimeUtc", typeof(DateTime));
        table.Columns.Add("IntervalMinutes", typeof(int));
        foreach (var name in new[] { "TemperatureAvgC", "TemperatureMinC", "TemperatureMaxC", "HumidityAvg", "DewPointC", "WindAvgMs", "GustMaxMs", "WindDirDeg", "RainMm", "PressureHpa", "IndoorTemperatureC", "IndoorHumidity" })
        {
            table.Columns.Add(name, typeof(double));
        }

        foreach (var o in rows)
        {
            table.Rows.Add(
                source, o.TimeUtc, o.IntervalMinutes, Db(o.TemperatureAvgC), Db(o.TemperatureMinC), Db(o.TemperatureMaxC),
                Db(o.HumidityAvg), Db(o.DewPointC), Db(o.WindAvgMs), Db(o.GustMaxMs), Db(o.WindDirDeg), Db(o.RainMm),
                Db(o.PressureHpa), Db(o.IndoorTemperatureC), Db(o.IndoorHumidity));
        }

        return table;
    }

    private static object Db<T>(T? value) where T : struct => value is { } v ? v : DBNull.Value;

    private static double? Real(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
