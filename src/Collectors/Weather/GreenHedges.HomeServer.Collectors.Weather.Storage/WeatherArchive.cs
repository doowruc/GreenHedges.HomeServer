using System.Globalization;
using GreenHedges.HomeServer.Common.Sqlite;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using Microsoft.Data.Sqlite;

namespace GreenHedges.HomeServer.Collectors.Weather.Storage;

/// <summary>
/// The SQLite archive: every line rtl_433 writes, kept as received, forever. <c>messages</c> is
/// insert-only and its ids increase in arrival order; <c>station_readings</c> is a typed copy of the
/// station's messages (rebuildable from <c>messages</c>); <c>devices</c> summarises every device heard.
/// </summary>
public sealed class WeatherArchive
{
    // Fixed-width UTC text sorts in time order and is easy to read in the sqlite3 shell.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    // Append-only. Each entry upgrades the schema by one version (tracked in PRAGMA user_version).
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE messages (
            id              INTEGER PRIMARY KEY,
            received_at_utc TEXT    NOT NULL,
            rtl_time        TEXT,
            protocol        INTEGER,
            model           TEXT,
            device_id       TEXT,
            channel         TEXT,
            is_valid_json   INTEGER NOT NULL,
            raw_json        TEXT    NOT NULL
        );
        CREATE INDEX messages_by_device ON messages (model, device_id, received_at_utc);

        CREATE TABLE station_readings (
            message_id      INTEGER PRIMARY KEY REFERENCES messages (id),
            received_at_utc TEXT    NOT NULL,
            device_id       TEXT    NOT NULL,
            temperature_c   REAL,
            humidity        REAL,
            wind_avg_ms     REAL,
            wind_max_ms     REAL,
            wind_dir_deg    REAL,
            rain_total_mm   REAL,
            battery_ok      INTEGER,
            flags           INTEGER,
            startup         INTEGER,
            rssi            REAL,
            snr             REAL
        );
        CREATE INDEX station_readings_by_time ON station_readings (received_at_utc);

        CREATE TABLE devices (
            model          TEXT    NOT NULL,
            device_id      TEXT    NOT NULL,
            first_seen_utc TEXT    NOT NULL,
            last_seen_utc  TEXT    NOT NULL,
            message_count  INTEGER NOT NULL,
            is_station     INTEGER NOT NULL,
            PRIMARY KEY (model, device_id)
        );
        """,
    ];

    private readonly SqliteDatabase _database;

    public WeatherArchive(string databasePath) => _database = new SqliteDatabase(databasePath, Migrations);

    public static int SchemaVersion => Migrations.Length;

    /// <summary>Creates the database if needed and applies any pending migrations.</summary>
    public Task InitialiseAsync(CancellationToken cancellationToken) => _database.InitialiseAsync(cancellationToken);

    /// <inheritdoc cref="SqliteDatabase.BackupToAsync"/>
    public Task BackupToAsync(string destinationPath, CancellationToken cancellationToken) =>
        _database.BackupToAsync(destinationPath, cancellationToken);

    /// <summary>
    /// Archives one message in a single transaction: the raw line, the typed station reading if
    /// <paramref name="isStation"/>, and the device summary. Returns the message's id.
    /// </summary>
    /// <param name="receivedAtUtc">The collector's clock when the line arrived: the archive's key time.</param>
    /// <param name="isStation">Whether the message is from one of the configured station's device ids.</param>
    public async Task<long> AppendAsync(Rtl433Message message, DateTimeOffset receivedAtUtc, bool isStation, CancellationToken cancellationToken)
    {
        var receivedAt = Format(receivedAtUtc);

        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        long id;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO messages (received_at_utc, rtl_time, protocol, model, device_id, channel, is_valid_json, raw_json)
                VALUES ($received_at_utc, $rtl_time, $protocol, $model, $device_id, $channel, $is_valid_json, $raw_json)
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$received_at_utc", receivedAt);
            command.Parameters.AddWithValue("$rtl_time", Db(message.Time));
            command.Parameters.AddWithValue("$protocol", Db(message.Protocol));
            command.Parameters.AddWithValue("$model", Db(message.Model));
            command.Parameters.AddWithValue("$device_id", Db(message.DeviceId));
            command.Parameters.AddWithValue("$channel", Db(message.Channel));
            command.Parameters.AddWithValue("$is_valid_json", message.IsValidJson ? 1 : 0);
            command.Parameters.AddWithValue("$raw_json", message.Raw);
            id = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        if (isStation && message is { DeviceId: { } deviceId, Weather: { } weather })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO station_readings (
                    message_id, received_at_utc, device_id, temperature_c, humidity, wind_avg_ms, wind_max_ms,
                    wind_dir_deg, rain_total_mm, battery_ok, flags, startup, rssi, snr)
                VALUES (
                    $message_id, $received_at_utc, $device_id, $temperature_c, $humidity, $wind_avg_ms, $wind_max_ms,
                    $wind_dir_deg, $rain_total_mm, $battery_ok, $flags, $startup, $rssi, $snr);
                """;
            command.Parameters.AddWithValue("$message_id", id);
            command.Parameters.AddWithValue("$received_at_utc", receivedAt);
            command.Parameters.AddWithValue("$device_id", deviceId);
            command.Parameters.AddWithValue("$temperature_c", Db(weather.TemperatureC));
            command.Parameters.AddWithValue("$humidity", Db(weather.Humidity));
            command.Parameters.AddWithValue("$wind_avg_ms", Db(weather.WindAvgMs));
            command.Parameters.AddWithValue("$wind_max_ms", Db(weather.WindMaxMs));
            command.Parameters.AddWithValue("$wind_dir_deg", Db(weather.WindDirDeg));
            command.Parameters.AddWithValue("$rain_total_mm", Db(weather.RainMm));
            command.Parameters.AddWithValue("$battery_ok", Db(weather.BatteryOk));
            command.Parameters.AddWithValue("$flags", Db(weather.Flags));
            command.Parameters.AddWithValue("$startup", Db(weather.Startup));
            command.Parameters.AddWithValue("$rssi", Db(weather.Rssi));
            command.Parameters.AddWithValue("$snr", Db(weather.Snr));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (message is { Model: { } model, DeviceId: { } device })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO devices (model, device_id, first_seen_utc, last_seen_utc, message_count, is_station)
                VALUES ($model, $device_id, $seen, $seen, 1, $is_station)
                ON CONFLICT (model, device_id) DO UPDATE SET
                    last_seen_utc = excluded.last_seen_utc,
                    message_count = message_count + 1,
                    is_station = excluded.is_station;
                """;
            command.Parameters.AddWithValue("$model", model);
            command.Parameters.AddWithValue("$device_id", device);
            command.Parameters.AddWithValue("$seen", receivedAt);
            command.Parameters.AddWithValue("$is_station", isStation ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Every device heard, most recently heard first.</summary>
    public async Task<IReadOnlyList<DeviceSummary>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT model, device_id, first_seen_utc, last_seen_utc, message_count, is_station
            FROM devices ORDER BY last_seen_utc DESC, model, device_id;
            """;

        var devices = new List<DeviceSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new DeviceSummary(
                reader.GetString(0),
                reader.GetString(1),
                Parse(reader.GetString(2)),
                Parse(reader.GetString(3)),
                reader.GetInt64(4),
                reader.GetInt64(5) != 0));
        }

        return devices;
    }

    /// <summary>The newest station reading, if any.</summary>
    public async Task<StationReading?> GetLatestStationReadingAsync(CancellationToken cancellationToken)
    {
        var readings = await ReadStationReadingsAsync(
            "ORDER BY message_id DESC LIMIT 1", static _ => { }, cancellationToken);
        return readings.Count > 0 ? readings[0] : null;
    }

    /// <summary>Station readings with ids after <paramref name="afterId"/>, in id (arrival) order: at most <paramref name="limit"/>.</summary>
    public Task<IReadOnlyList<StationReading>> ReadStationReadingsAfterAsync(long afterId, int limit, CancellationToken cancellationToken) =>
        ReadStationReadingsAsync(
            "WHERE message_id > $after_id ORDER BY message_id LIMIT $limit",
            parameters =>
            {
                parameters.AddWithValue("$after_id", afterId);
                parameters.AddWithValue("$limit", limit);
            },
            cancellationToken);

    /// <summary>
    /// Station readings received at or after <paramref name="fromUtc"/> with ids up to <paramref name="throughId"/>,
    /// in id order.
    /// </summary>
    public Task<IReadOnlyList<StationReading>> ReadStationReadingsFromAsync(
        DateTimeOffset fromUtc, long throughId, CancellationToken cancellationToken) =>
        ReadStationReadingsAsync(
            "WHERE received_at_utc >= $from AND message_id <= $through_id ORDER BY message_id",
            parameters =>
            {
                parameters.AddWithValue("$from", Format(fromUtc));
                parameters.AddWithValue("$through_id", throughId);
            },
            cancellationToken);

    /// <summary>
    /// The latest station reading received before <paramref name="beforeUtc"/>; with <paramref name="withRain"/>, the
    /// latest that carries a rain total.
    /// </summary>
    public async Task<StationReading?> GetLatestStationReadingBeforeAsync(
        DateTimeOffset beforeUtc, bool withRain, CancellationToken cancellationToken)
    {
        var readings = await ReadStationReadingsAsync(
            $"WHERE received_at_utc < $before{(withRain ? " AND rain_total_mm IS NOT NULL" : "")} ORDER BY received_at_utc DESC, message_id DESC LIMIT 1",
            parameters => parameters.AddWithValue("$before", Format(beforeUtc)),
            cancellationToken);
        return readings.Count > 0 ? readings[0] : null;
    }

    private async Task<IReadOnlyList<StationReading>> ReadStationReadingsAsync(
        string clauses, Action<SqliteParameterCollection> addParameters, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT message_id, received_at_utc, device_id, temperature_c, humidity, wind_avg_ms, wind_max_ms,
                   wind_dir_deg, rain_total_mm, battery_ok, flags, startup, rssi, snr
            FROM station_readings {clauses};
            """;
        addParameters(command.Parameters);

        var readings = new List<StationReading>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            readings.Add(new StationReading(
                reader.GetInt64(0),
                Parse(reader.GetString(1)),
                reader.GetString(2),
                new WeatherFields(
                    Real(reader, 3), Real(reader, 4), Real(reader, 5), Real(reader, 6), Real(reader, 7), Real(reader, 8),
                    Int(reader, 9), Int(reader, 10), Int(reader, 11), Real(reader, 12), Real(reader, 13))));
        }

        return readings;
    }

    /// <summary>The number of archived messages.</summary>
    public async Task<long> CountMessagesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM messages;";
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static object Db<T>(T? value) => (object?)value ?? DBNull.Value;

    private static double? Real(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static int? Int(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string text) =>
        DateTimeOffset.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}

/// <summary>A device heard on the air, with how often and when.</summary>
/// <param name="IsStation">Whether its latest message was from a configured station id.</param>
public sealed record DeviceSummary(
    string Model,
    string DeviceId,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    long MessageCount,
    bool IsStation);

/// <summary>A typed station reading, as stored.</summary>
public sealed record StationReading(long MessageId, DateTimeOffset ReceivedAtUtc, string DeviceId, WeatherFields Weather);
