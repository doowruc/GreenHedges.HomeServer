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
    private const string DateFormat = "yyyy-MM-dd";

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
        // v2: Weather Underground's history of the station (the console's uploads): each day's API response as
        // received, and a typed copy of its observations (rebuildable from the responses). Metric units, as requested.
        """
        CREATE TABLE wunderground_days (
            local_date        TEXT    PRIMARY KEY,
            fetched_at_utc    TEXT    NOT NULL,
            observation_count INTEGER NOT NULL,
            raw_json          TEXT    NOT NULL
        );
        CREATE INDEX wunderground_days_by_fetch ON wunderground_days (fetched_at_utc);

        CREATE TABLE wunderground_observations (
            epoch             INTEGER PRIMARY KEY,
            obs_time_utc      TEXT    NOT NULL,
            local_date        TEXT    NOT NULL REFERENCES wunderground_days (local_date),
            temp_avg_c        REAL,
            temp_high_c       REAL,
            temp_low_c        REAL,
            humidity_avg      REAL,
            humidity_high     REAL,
            humidity_low      REAL,
            dewpt_avg_c       REAL,
            wind_avg_kmh      REAL,
            wind_high_kmh     REAL,
            gust_high_kmh     REAL,
            wind_dir_avg_deg  REAL,
            pressure_max_hpa  REAL,
            pressure_min_hpa  REAL,
            precip_rate_mmh   REAL,
            precip_total_mm   REAL,
            qc_status         INTEGER
        );
        CREATE INDEX wunderground_observations_by_date ON wunderground_observations (local_date, epoch);
        """,
        // v3: Weathercloud's exports of the station (10-minute rows, with the console's indoor readings and pressure):
        // each row's line as it was in the file, and typed values in fixed units (°C, %, m/s, hPa, mm). Stored by local
        // day; a day's version changes each time it's imported, for publishing.
        """
        CREATE TABLE weathercloud_days (
            local_date      TEXT    PRIMARY KEY,
            version         INTEGER NOT NULL UNIQUE,
            imported_at_utc TEXT    NOT NULL,
            source_file     TEXT    NOT NULL,
            row_count       INTEGER NOT NULL
        );

        CREATE TABLE weathercloud_rows (
            time_utc        TEXT    PRIMARY KEY,
            local_date      TEXT    NOT NULL REFERENCES weathercloud_days (local_date),
            local_time      TEXT    NOT NULL,
            raw_line        TEXT    NOT NULL,
            temp_in_c       REAL,
            temp_c          REAL,
            dew_c           REAL,
            humidity_in     REAL,
            humidity        REAL,
            gust_ms         REAL,
            wind_avg_ms     REAL,
            wind_dir_deg    REAL,
            pressure_hpa    REAL,
            rain_day_mm     REAL,
            rain_rate_mmh   REAL
        );
        CREATE INDEX weathercloud_rows_by_date ON weathercloud_rows (local_date, time_utc);
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

    /// <summary>When each Weather Underground day was last fetched, by local date.</summary>
    public async Task<IReadOnlyDictionary<DateOnly, DateTimeOffset>> GetWundergroundDaysAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT local_date, fetched_at_utc FROM wunderground_days;";

        var days = new Dictionary<DateOnly, DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            days[DateOnly.ParseExact(reader.GetString(0), DateFormat, CultureInfo.InvariantCulture)] = Parse(reader.GetString(1));
        }

        return days;
    }

    /// <summary>
    /// Stores one Weather Underground day: the response as received and its observations, replacing any earlier fetch of
    /// the same day, in one transaction.
    /// </summary>
    public async Task SaveWundergroundDayAsync(
        DateOnly localDate, DateTimeOffset fetchedAtUtc, string rawJson, IReadOnlyList<WundergroundObservation> observations,
        CancellationToken cancellationToken)
    {
        var date = localDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                DELETE FROM wunderground_observations WHERE local_date = $date;
                INSERT INTO wunderground_days (local_date, fetched_at_utc, observation_count, raw_json)
                VALUES ($date, $fetched, $count, $raw)
                ON CONFLICT (local_date) DO UPDATE SET
                    fetched_at_utc = excluded.fetched_at_utc,
                    observation_count = excluded.observation_count,
                    raw_json = excluded.raw_json;
                """;
            command.Parameters.AddWithValue("$date", date);
            command.Parameters.AddWithValue("$fetched", Format(fetchedAtUtc));
            command.Parameters.AddWithValue("$count", observations.Count);
            command.Parameters.AddWithValue("$raw", rawJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT OR REPLACE INTO wunderground_observations (
                    epoch, obs_time_utc, local_date, temp_avg_c, temp_high_c, temp_low_c, humidity_avg, humidity_high,
                    humidity_low, dewpt_avg_c, wind_avg_kmh, wind_high_kmh, gust_high_kmh, wind_dir_avg_deg,
                    pressure_max_hpa, pressure_min_hpa, precip_rate_mmh, precip_total_mm, qc_status)
                VALUES (
                    $epoch, $time, $date, $temp_avg, $temp_high, $temp_low, $hum_avg, $hum_high, $hum_low, $dewpt,
                    $wind_avg, $wind_high, $gust_high, $wind_dir, $pres_max, $pres_min, $precip_rate, $precip_total, $qc);
                """;
            var p = insert.Parameters;
            foreach (var name in new[] { "$epoch", "$time", "$date", "$temp_avg", "$temp_high", "$temp_low", "$hum_avg", "$hum_high", "$hum_low", "$dewpt", "$wind_avg", "$wind_high", "$gust_high", "$wind_dir", "$pres_max", "$pres_min", "$precip_rate", "$precip_total", "$qc" })
            {
                p.Add(new SqliteParameter { ParameterName = name });
            }

            foreach (var o in observations)
            {
                p["$epoch"].Value = o.Epoch;
                p["$time"].Value = Format(o.ObsTimeUtc);
                p["$date"].Value = date;
                p["$temp_avg"].Value = Db(o.TempAvgC);
                p["$temp_high"].Value = Db(o.TempHighC);
                p["$temp_low"].Value = Db(o.TempLowC);
                p["$hum_avg"].Value = Db(o.HumidityAvg);
                p["$hum_high"].Value = Db(o.HumidityHigh);
                p["$hum_low"].Value = Db(o.HumidityLow);
                p["$dewpt"].Value = Db(o.DewPointAvgC);
                p["$wind_avg"].Value = Db(o.WindAvgKmh);
                p["$wind_high"].Value = Db(o.WindHighKmh);
                p["$gust_high"].Value = Db(o.GustHighKmh);
                p["$wind_dir"].Value = Db(o.WindDirAvgDeg);
                p["$pres_max"].Value = Db(o.PressureMaxHpa);
                p["$pres_min"].Value = Db(o.PressureMinHpa);
                p["$precip_rate"].Value = Db(o.PrecipRateMmh);
                p["$precip_total"].Value = Db(o.PrecipTotalMm);
                p["$qc"].Value = Db(o.QcStatus);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Weather Underground days fetched after <paramref name="afterUtc"/>, oldest fetch first (at most
    /// <paramref name="limit"/>), each with its observations in time order: what's new to publish.
    /// </summary>
    public async Task<IReadOnlyList<WundergroundDay>> ReadWundergroundDaysFetchedAfterAsync(
        DateTimeOffset afterUtc, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var days = new List<(DateOnly Date, DateTimeOffset Fetched)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT local_date, fetched_at_utc FROM wunderground_days
                WHERE fetched_at_utc > $after ORDER BY fetched_at_utc, local_date LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$after", Format(afterUtc));
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                days.Add((DateOnly.ParseExact(reader.GetString(0), DateFormat, CultureInfo.InvariantCulture), Parse(reader.GetString(1))));
            }
        }

        var result = new List<WundergroundDay>();
        foreach (var (date, fetched) in days)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT epoch, obs_time_utc, temp_avg_c, temp_high_c, temp_low_c, humidity_avg, humidity_high, humidity_low,
                       dewpt_avg_c, wind_avg_kmh, wind_high_kmh, gust_high_kmh, wind_dir_avg_deg, pressure_max_hpa,
                       pressure_min_hpa, precip_rate_mmh, precip_total_mm, qc_status
                FROM wunderground_observations WHERE local_date = $date ORDER BY epoch;
                """;
            command.Parameters.AddWithValue("$date", date.ToString(DateFormat, CultureInfo.InvariantCulture));
            var observations = new List<WundergroundObservation>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    observations.Add(new WundergroundObservation(
                        reader.GetInt64(0), Parse(reader.GetString(1)),
                        Real(reader, 2), Real(reader, 3), Real(reader, 4), Real(reader, 5), Real(reader, 6), Real(reader, 7),
                        Real(reader, 8), Real(reader, 9), Real(reader, 10), Real(reader, 11), Real(reader, 12), Real(reader, 13),
                        Real(reader, 14), Real(reader, 15), Real(reader, 16), Int(reader, 17)));
                }
            }

            result.Add(new WundergroundDay(date, fetched, observations));
        }

        return result;
    }

    /// <summary>
    /// Stores Weathercloud rows, a local day at a time: each day's rows replace any it had, and the day gets a new
    /// version (so it's published again). All in one transaction.
    /// </summary>
    public async Task SaveWeathercloudDaysAsync(
        IReadOnlyList<WeathercloudDay> days, string sourceFile, DateTimeOffset importedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var day = connection.CreateCommand();
        day.Transaction = transaction;
        day.CommandText =
            """
            DELETE FROM weathercloud_rows WHERE local_date = $date;
            INSERT INTO weathercloud_days (local_date, version, imported_at_utc, source_file, row_count)
            VALUES ($date, (SELECT IFNULL(MAX(version), 0) + 1 FROM weathercloud_days), $imported, $file, $count)
            ON CONFLICT (local_date) DO UPDATE SET
                version = (SELECT IFNULL(MAX(version), 0) + 1 FROM weathercloud_days),
                imported_at_utc = excluded.imported_at_utc,
                source_file = excluded.source_file,
                row_count = excluded.row_count;
            """;
        foreach (var name in new[] { "$date", "$imported", "$file", "$count" })
        {
            day.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        await using var row = connection.CreateCommand();
        row.Transaction = transaction;
        row.CommandText =
            """
            INSERT OR REPLACE INTO weathercloud_rows (
                time_utc, local_date, local_time, raw_line, temp_in_c, temp_c, dew_c, humidity_in, humidity, gust_ms,
                wind_avg_ms, wind_dir_deg, pressure_hpa, rain_day_mm, rain_rate_mmh)
            VALUES ($time, $date, $local, $raw, $temp_in, $temp, $dew, $hum_in, $hum, $gust, $wind, $dir, $pressure, $rain, $rate);
            """;
        foreach (var name in new[] { "$time", "$date", "$local", "$raw", "$temp_in", "$temp", "$dew", "$hum_in", "$hum", "$gust", "$wind", "$dir", "$pressure", "$rain", "$rate" })
        {
            row.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        foreach (var d in days)
        {
            var date = d.LocalDate.ToString(DateFormat, CultureInfo.InvariantCulture);
            day.Parameters["$date"].Value = date;
            day.Parameters["$imported"].Value = Format(importedAtUtc);
            day.Parameters["$file"].Value = sourceFile;
            day.Parameters["$count"].Value = d.Rows.Count;
            await day.ExecuteNonQueryAsync(cancellationToken);

            foreach (var w in d.Rows)
            {
                var p = row.Parameters;
                p["$time"].Value = Format(w.TimeUtc);
                p["$date"].Value = date;
                p["$local"].Value = w.LocalTime;
                p["$raw"].Value = w.RawLine;
                p["$temp_in"].Value = Db(w.IndoorTemperatureC);
                p["$temp"].Value = Db(w.TemperatureC);
                p["$dew"].Value = Db(w.DewPointC);
                p["$hum_in"].Value = Db(w.IndoorHumidity);
                p["$hum"].Value = Db(w.Humidity);
                p["$gust"].Value = Db(w.GustMs);
                p["$wind"].Value = Db(w.WindAvgMs);
                p["$dir"].Value = Db(w.WindDirDeg);
                p["$pressure"].Value = Db(w.PressureHpa);
                p["$rain"].Value = Db(w.RainDayMm);
                p["$rate"].Value = Db(w.RainRateMmh);
                await row.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Weathercloud days with a version after <paramref name="afterVersion"/>, lowest first (at most
    /// <paramref name="limit"/>), each with its rows in time order: what's new to publish.
    /// </summary>
    public async Task<IReadOnlyList<WeathercloudStoredDay>> ReadWeathercloudDaysAfterAsync(
        long afterVersion, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var days = new List<(DateOnly Date, long Version)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT local_date, version FROM weathercloud_days WHERE version > $after ORDER BY version LIMIT $limit;";
            command.Parameters.AddWithValue("$after", afterVersion);
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                days.Add((DateOnly.ParseExact(reader.GetString(0), DateFormat, CultureInfo.InvariantCulture), reader.GetInt64(1)));
            }
        }

        var result = new List<WeathercloudStoredDay>();
        foreach (var (date, version) in days)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT time_utc, local_time, raw_line, temp_in_c, temp_c, dew_c, humidity_in, humidity, gust_ms, wind_avg_ms,
                       wind_dir_deg, pressure_hpa, rain_day_mm, rain_rate_mmh
                FROM weathercloud_rows WHERE local_date = $date ORDER BY time_utc;
                """;
            command.Parameters.AddWithValue("$date", date.ToString(DateFormat, CultureInfo.InvariantCulture));
            var rows = new List<WeathercloudRow>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new WeathercloudRow(
                        Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                        Real(reader, 3), Real(reader, 4), Real(reader, 5), Real(reader, 6), Real(reader, 7), Real(reader, 8),
                        Real(reader, 9), Real(reader, 10), Real(reader, 11), Real(reader, 12), Real(reader, 13)));
                }
            }

            result.Add(new WeathercloudStoredDay(date, version, rows));
        }

        return result;
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

/// <summary>
/// One Weather Underground observation of the station: a 5-minute interval ending at <see cref="ObsTimeUtc"/>, as the
/// console uploaded it, in metric units (km/h, hPa, mm). <see cref="PrecipTotalMm"/> is the running total for the local day.
/// </summary>
public sealed record WundergroundObservation(
    long Epoch,
    DateTimeOffset ObsTimeUtc,
    double? TempAvgC,
    double? TempHighC,
    double? TempLowC,
    double? HumidityAvg,
    double? HumidityHigh,
    double? HumidityLow,
    double? DewPointAvgC,
    double? WindAvgKmh,
    double? WindHighKmh,
    double? GustHighKmh,
    double? WindDirAvgDeg,
    double? PressureMaxHpa,
    double? PressureMinHpa,
    double? PrecipRateMmh,
    double? PrecipTotalMm,
    int? QcStatus);

/// <summary>A fetched Weather Underground day (a local date) and its observations, in time order.</summary>
public sealed record WundergroundDay(DateOnly LocalDate, DateTimeOffset FetchedAtUtc, IReadOnlyList<WundergroundObservation> Observations);

/// <summary>
/// One Weathercloud row: the 10 minutes ending at <see cref="TimeUtc"/>, as exported, in fixed units (°C, %, m/s, hPa,
/// mm). <see cref="RainDayMm"/> is the running total for the local day.
/// </summary>
/// <param name="LocalTime">The row's time as written in the file (local time).</param>
/// <param name="RawLine">The row's line as it was in the file.</param>
public sealed record WeathercloudRow(
    DateTimeOffset TimeUtc,
    string LocalTime,
    string RawLine,
    double? IndoorTemperatureC,
    double? TemperatureC,
    double? DewPointC,
    double? IndoorHumidity,
    double? Humidity,
    double? GustMs,
    double? WindAvgMs,
    double? WindDirDeg,
    double? PressureHpa,
    double? RainDayMm,
    double? RainRateMmh);

/// <summary>A local day's Weathercloud rows, to store.</summary>
public sealed record WeathercloudDay(DateOnly LocalDate, IReadOnlyList<WeathercloudRow> Rows);

/// <summary>A stored Weathercloud day, with the version it was given when imported, and its rows in time order.</summary>
public sealed record WeathercloudStoredDay(DateOnly LocalDate, long Version, IReadOnlyList<WeathercloudRow> Rows);
