using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GreenHedges.HomeServer.Common.Sqlite;
using Microsoft.Data.Sqlite;

namespace GreenHedges.HomeServer.Collectors.Libre.Storage;

/// <summary>
/// The SQLite archive: the single source of truth for every reading collected.
/// Rows are insert-or-ignore on (timestamp_utc, source) and never updated or deleted, so ids
/// increase in insertion order and can be used as watermarks by downstream publishers.
/// </summary>
public sealed class GlucoseArchive
{
    // Fixed-width UTC text sorts in time order and is easy to read in the sqlite3 shell.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string ReceivedAtFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    // Append-only. Each entry upgrades the schema by one version (tracked in PRAGMA user_version).
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE readings (
            timestamp_utc     TEXT    NOT NULL,
            source            TEXT    NOT NULL CHECK (source IN ('current', 'graph')),
            timestamp_local   TEXT,
            value_mgdl        REAL    NOT NULL,
            trend_arrow       INTEGER,
            measurement_color INTEGER,
            is_high           INTEGER NOT NULL,
            is_low            INTEGER NOT NULL,
            received_at_utc   TEXT    NOT NULL,
            raw_json          TEXT    NOT NULL,
            PRIMARY KEY (timestamp_utc, source)
        );
        """,

        // v2: an explicit, stable id (an implicit rowid may be renumbered by VACUUM), keeping
        // existing rowids so insertion order is preserved; plus per-target publishing watermarks.
        """
        CREATE TABLE readings_v2 (
            id                INTEGER PRIMARY KEY,
            timestamp_utc     TEXT    NOT NULL,
            source            TEXT    NOT NULL CHECK (source IN ('current', 'graph')),
            timestamp_local   TEXT,
            value_mgdl        REAL    NOT NULL,
            trend_arrow       INTEGER,
            measurement_color INTEGER,
            is_high           INTEGER NOT NULL,
            is_low            INTEGER NOT NULL,
            received_at_utc   TEXT    NOT NULL,
            raw_json          TEXT    NOT NULL,
            UNIQUE (timestamp_utc, source)
        );
        INSERT INTO readings_v2 (
            id, timestamp_utc, source, timestamp_local, value_mgdl, trend_arrow, measurement_color,
            is_high, is_low, received_at_utc, raw_json)
        SELECT
            rowid, timestamp_utc, source, timestamp_local, value_mgdl, trend_arrow, measurement_color,
            is_high, is_low, received_at_utc, raw_json
        FROM readings ORDER BY rowid;
        DROP TABLE readings;
        ALTER TABLE readings_v2 RENAME TO readings;

        CREATE TABLE publish_state (
            target  TEXT    PRIMARY KEY,
            last_id INTEGER NOT NULL
        );
        """,

        // v3: sensors seen on the connection, and the sensor in use for each current reading.
        """
        CREATE TABLE sensors (
            serial           TEXT    PRIMARY KEY,
            activated_at_utc TEXT,
            warmup_minutes   INTEGER,
            product_type     INTEGER,
            first_seen_utc   TEXT    NOT NULL,
            last_seen_utc    TEXT    NOT NULL,
            raw_json         TEXT    NOT NULL
        );
        ALTER TABLE readings ADD COLUMN sensor_serial TEXT;
        """,

        // v4: allow imported sources (keeping ids), and an events table for non-glucose records.
        """
        CREATE TABLE readings_v4 (
            id                INTEGER PRIMARY KEY,
            timestamp_utc     TEXT    NOT NULL,
            source            TEXT    NOT NULL CHECK (source IN ('current', 'graph', 'libreview', 'libreview_scan')),
            timestamp_local   TEXT,
            value_mgdl        REAL    NOT NULL,
            trend_arrow       INTEGER,
            measurement_color INTEGER,
            is_high           INTEGER NOT NULL,
            is_low            INTEGER NOT NULL,
            received_at_utc   TEXT    NOT NULL,
            raw_json          TEXT    NOT NULL,
            sensor_serial     TEXT,
            UNIQUE (timestamp_utc, source)
        );
        INSERT INTO readings_v4 (
            id, timestamp_utc, source, timestamp_local, value_mgdl, trend_arrow, measurement_color,
            is_high, is_low, received_at_utc, raw_json, sensor_serial)
        SELECT
            id, timestamp_utc, source, timestamp_local, value_mgdl, trend_arrow, measurement_color,
            is_high, is_low, received_at_utc, raw_json, sensor_serial
        FROM readings ORDER BY id;
        DROP TABLE readings;
        ALTER TABLE readings_v4 RENAME TO readings;

        CREATE TABLE events (
            id              INTEGER PRIMARY KEY,
            timestamp_utc   TEXT    NOT NULL,
            timestamp_local TEXT,
            kind            TEXT    NOT NULL,
            value           REAL,
            unit            TEXT,
            text            TEXT,
            source          TEXT    NOT NULL,
            received_at_utc TEXT    NOT NULL,
            raw_json        TEXT    NOT NULL,
            dedup_key       TEXT    NOT NULL UNIQUE
        );
        CREATE INDEX events_by_time ON events (timestamp_utc);
        """,
    ];

    private readonly SqliteDatabase _database;

    public GlucoseArchive(string databasePath) => _database = new SqliteDatabase(databasePath, Migrations);

    public static int SchemaVersion => Migrations.Length;

    /// <summary>Creates the database if needed and applies any pending migrations.</summary>
    public Task InitialiseAsync(CancellationToken cancellationToken) => _database.InitialiseAsync(cancellationToken);

    /// <summary>Inserts readings, ignoring any already archived. Returns the number of new rows.</summary>
    public async Task<int> InsertAsync(IReadOnlyCollection<ArchivedReading> readings, CancellationToken cancellationToken)
    {
        if (readings.Count == 0)
        {
            return 0;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO readings (
                timestamp_utc, source, timestamp_local, value_mgdl, trend_arrow, measurement_color,
                is_high, is_low, received_at_utc, raw_json, sensor_serial)
            VALUES (
                $timestamp_utc, $source, $timestamp_local, $value_mgdl, $trend_arrow, $measurement_color,
                $is_high, $is_low, $received_at_utc, $raw_json, $sensor_serial);
            """;

        var timestampUtc = command.Parameters.Add("$timestamp_utc", SqliteType.Text);
        var source = command.Parameters.Add("$source", SqliteType.Text);
        var timestampLocal = command.Parameters.Add("$timestamp_local", SqliteType.Text);
        var valueMgdl = command.Parameters.Add("$value_mgdl", SqliteType.Real);
        var trendArrow = command.Parameters.Add("$trend_arrow", SqliteType.Integer);
        var measurementColor = command.Parameters.Add("$measurement_color", SqliteType.Integer);
        var isHigh = command.Parameters.Add("$is_high", SqliteType.Integer);
        var isLow = command.Parameters.Add("$is_low", SqliteType.Integer);
        var receivedAtUtc = command.Parameters.Add("$received_at_utc", SqliteType.Text);
        var rawJson = command.Parameters.Add("$raw_json", SqliteType.Text);
        var sensorSerial = command.Parameters.Add("$sensor_serial", SqliteType.Text);

        var inserted = 0;
        foreach (var reading in readings)
        {
            timestampUtc.Value = FormatTimestamp(reading.TimestampUtc);
            source.Value = ToDbValue(reading.Source);
            timestampLocal.Value = (object?)reading.TimestampLocal ?? DBNull.Value;
            valueMgdl.Value = reading.ValueMgPerDl;
            trendArrow.Value = (object?)reading.TrendArrow ?? DBNull.Value;
            measurementColor.Value = (object?)reading.MeasurementColor ?? DBNull.Value;
            isHigh.Value = reading.IsHigh ? 1 : 0;
            isLow.Value = reading.IsLow ? 1 : 0;
            receivedAtUtc.Value = reading.ReceivedAtUtc.UtcDateTime.ToString(ReceivedAtFormat, CultureInfo.InvariantCulture);
            rawJson.Value = reading.RawJson;
            sensorSerial.Value = (object?)reading.SensorSerial ?? DBNull.Value;

            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    /// <summary>The sensor timestamp of the newest reading from <paramref name="source"/>, if any.</summary>
    public async Task<DateTimeOffset?> GetLatestTimestampAsync(ReadingSource source, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(timestamp_utc) FROM readings WHERE source = $source;";
        command.Parameters.AddWithValue("$source", ToDbValue(source));

        return await command.ExecuteScalarAsync(cancellationToken) is string text ? ParseTimestamp(text) : null;
    }

    public async Task<long> CountAsync(ReadingSource source, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM readings WHERE source = $source;";
        command.Parameters.AddWithValue("$source", ToDbValue(source));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Writes a consistent copy of the archive to <paramref name="destinationPath"/> (overwritten if it exists),
    /// safe while the collector is writing. The copy uses a rollback journal, so it's a single
    /// self-contained file, and it's integrity-checked before returning.
    /// </summary>
    public Task BackupToAsync(string destinationPath, CancellationToken cancellationToken) =>
        _database.BackupToAsync(destinationPath, cancellationToken);

    /// <summary>
    /// Readings with an id greater than <paramref name="afterId"/>, in insertion (id) order.
    /// Each is flagged if a reading from a higher-priority source (current, then graph, then libreview,
    /// then libreview_scan) lies within <paramref name="nearbyWindow"/> of its timestamp.
    /// </summary>
    public async Task<IReadOnlyList<StoredReading>> ReadAfterAsync(
        long afterId,
        int limit,
        TimeSpan nearbyWindow,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.id, r.timestamp_utc, r.source, r.timestamp_local, r.value_mgdl, r.trend_arrow,
                   r.measurement_color, r.is_high, r.is_low, r.received_at_utc, r.raw_json, r.sensor_serial,
                   EXISTS (
                       SELECT 1 FROM readings c
                       WHERE c.id <> r.id
                         AND c.timestamp_utc BETWEEN strftime('%Y-%m-%dT%H:%M:%SZ', r.timestamp_utc, $minus)
                                                 AND strftime('%Y-%m-%dT%H:%M:%SZ', r.timestamp_utc, $plus)
                         AND (CASE c.source WHEN 'current' THEN 0 WHEN 'graph' THEN 1 WHEN 'libreview' THEN 2 ELSE 3 END)
                           < (CASE r.source WHEN 'current' THEN 0 WHEN 'graph' THEN 1 WHEN 'libreview' THEN 2 ELSE 3 END)
                   ) AS has_nearby_better
            FROM readings r
            WHERE r.id > $after
            ORDER BY r.id
            LIMIT $limit;
            """;
        var seconds = (int)nearbyWindow.TotalSeconds;
        command.Parameters.AddWithValue("$after", afterId);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$minus", $"-{seconds} seconds");
        command.Parameters.AddWithValue("$plus", $"+{seconds} seconds");

        var results = new List<StoredReading>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var reading = new ArchivedReading(
                ParseTimestamp(reader.GetString(1)),
                FromDbValue(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetInt32(7) != 0,
                reader.GetInt32(8) != 0,
                DateTimeOffset.ParseExact(reader.GetString(9), ReceivedAtFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11));
            results.Add(new StoredReading(reader.GetInt64(0), reading, reader.GetInt32(12) != 0));
        }

        return results;
    }

    /// <summary>Inserts events, ignoring any already archived (same source, kind and raw record). Returns the number of new rows.</summary>
    public async Task<int> InsertEventsAsync(IReadOnlyCollection<ArchivedEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO events (
                timestamp_utc, timestamp_local, kind, value, unit, text, source, received_at_utc, raw_json, dedup_key)
            VALUES ($timestamp_utc, $timestamp_local, $kind, $value, $unit, $text, $source, $received_at_utc, $raw_json, $dedup_key);
            """;

        var timestampUtc = command.Parameters.Add("$timestamp_utc", SqliteType.Text);
        var timestampLocal = command.Parameters.Add("$timestamp_local", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        var value = command.Parameters.Add("$value", SqliteType.Real);
        var unit = command.Parameters.Add("$unit", SqliteType.Text);
        var text = command.Parameters.Add("$text", SqliteType.Text);
        var source = command.Parameters.Add("$source", SqliteType.Text);
        var receivedAtUtc = command.Parameters.Add("$received_at_utc", SqliteType.Text);
        var rawJson = command.Parameters.Add("$raw_json", SqliteType.Text);
        var dedupKey = command.Parameters.Add("$dedup_key", SqliteType.Text);

        var inserted = 0;
        foreach (var e in events)
        {
            timestampUtc.Value = FormatTimestamp(e.TimestampUtc);
            timestampLocal.Value = (object?)e.TimestampLocal ?? DBNull.Value;
            kind.Value = e.Kind;
            value.Value = (object?)e.Value ?? DBNull.Value;
            unit.Value = (object?)e.Unit ?? DBNull.Value;
            text.Value = (object?)e.Text ?? DBNull.Value;
            source.Value = e.Source;
            receivedAtUtc.Value = e.ReceivedAtUtc.UtcDateTime.ToString(ReceivedAtFormat, CultureInfo.InvariantCulture);
            rawJson.Value = e.RawJson;
            dedupKey.Value = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{e.Source}\n{e.Kind}\n{e.RawJson}")));
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async Task<long> CountEventsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events;";
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The id of the last reading published to <paramref name="target"/>, or 0 if none.</summary>
    public async Task<long> GetPublishedIdAsync(string target, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_id FROM publish_state WHERE target = $target;";
        command.Parameters.AddWithValue("$target", target);
        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : 0;
    }

    public async Task SetPublishedIdAsync(string target, long lastId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO publish_state (target, last_id) VALUES ($target, $last_id)
            ON CONFLICT (target) DO UPDATE SET last_id = excluded.last_id;
            """;
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$last_id", lastId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Records that <paramref name="serial"/> was seen at <paramref name="seenAt"/>: inserts it the first
    /// time, otherwise updates last-seen and the latest details. Returns true if the sensor is new to the archive.
    /// </summary>
    public async Task<bool> RecordSensorAsync(
        string serial,
        DateTimeOffset? activatedAt,
        int? warmupMinutes,
        int? productType,
        string rawJson,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO sensors (serial, activated_at_utc, warmup_minutes, product_type, first_seen_utc, last_seen_utc, raw_json)
            VALUES ($serial, $activated, $warmup, $product, $seen, $seen, $raw)
            ON CONFLICT (serial) DO NOTHING;
            """;
        AddSensorParameters(insert);
        var isNew = await insert.ExecuteNonQueryAsync(cancellationToken) == 1;

        if (!isNew)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE sensors SET
                    activated_at_utc = COALESCE($activated, activated_at_utc),
                    warmup_minutes   = COALESCE($warmup, warmup_minutes),
                    product_type     = COALESCE($product, product_type),
                    last_seen_utc    = $seen,
                    raw_json         = $raw
                WHERE serial = $serial;
                """;
            AddSensorParameters(update);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return isNew;

        void AddSensorParameters(SqliteCommand command)
        {
            command.Parameters.AddWithValue("$serial", serial);
            command.Parameters.AddWithValue("$activated", activatedAt is { } a ? FormatTimestamp(a) : DBNull.Value);
            command.Parameters.AddWithValue("$warmup", (object?)warmupMinutes ?? DBNull.Value);
            command.Parameters.AddWithValue("$product", (object?)productType ?? DBNull.Value);
            command.Parameters.AddWithValue("$seen", FormatTimestamp(seenAt));
            command.Parameters.AddWithValue("$raw", rawJson);
        }
    }

    /// <summary>The most recently seen sensor, if any.</summary>
    public async Task<ArchivedSensor?> GetLatestSensorAsync(CancellationToken cancellationToken) =>
        (await GetRecentSensorsAsync(1, cancellationToken)).FirstOrDefault();

    /// <summary>Up to <paramref name="count"/> sensors, most recently seen first.</summary>
    public async Task<IReadOnlyList<ArchivedSensor>> GetRecentSensorsAsync(int count, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT serial, activated_at_utc, warmup_minutes, product_type, first_seen_utc, last_seen_utc, raw_json
            FROM sensors ORDER BY last_seen_utc DESC, first_seen_utc DESC LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sensors = new List<ArchivedSensor>();
        while (await reader.ReadAsync(cancellationToken))
        {
            sensors.Add(new ArchivedSensor(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : ParseTimestamp(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ParseTimestamp(reader.GetString(4)),
                ParseTimestamp(reader.GetString(5)),
                reader.GetString(6)));
        }

        return sensors;
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) => _database.OpenAsync(cancellationToken);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string text) =>
        DateTimeOffset.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static ReadingSource FromDbValue(string source) => source switch
    {
        "current" => ReadingSource.Current,
        "graph" => ReadingSource.Graph,
        "libreview" => ReadingSource.LibreView,
        "libreview_scan" => ReadingSource.LibreViewScan,
        _ => throw new InvalidOperationException($"Unknown reading source '{source}' in database."),
    };

    private static string ToDbValue(ReadingSource source) => source switch
    {
        ReadingSource.Current => "current",
        ReadingSource.Graph => "graph",
        ReadingSource.LibreView => "libreview",
        ReadingSource.LibreViewScan => "libreview_scan",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
