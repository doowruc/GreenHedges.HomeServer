using Microsoft.Data.Sqlite;

namespace GreenHedges.HomeServer.Collectors.Libre.Storage.Tests;

public sealed class SchemaV2Tests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 2, 26, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"librecollector-v2-test-{Guid.NewGuid():N}.db");
    private readonly GlucoseArchive _archive;

    public SchemaV2Tests() => _archive = new GlucoseArchive(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private static ArchivedReading Reading(DateTimeOffset timestamp, ReadingSource source, double value = 115, int? trend = 3) =>
        new(timestamp, source, null, value, trend, 1, false, false, T0, "{}");

    [Fact]
    public async Task Upgrade_from_v1_keeps_rows_ids_and_order()
    {
        // A v1 database as deployed before this change: implicit rowids, graph rows inserted after newer current rows.
        await using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE readings (
                    timestamp_utc TEXT NOT NULL, source TEXT NOT NULL CHECK (source IN ('current', 'graph')),
                    timestamp_local TEXT, value_mgdl REAL NOT NULL, trend_arrow INTEGER, measurement_color INTEGER,
                    is_high INTEGER NOT NULL, is_low INTEGER NOT NULL, received_at_utc TEXT NOT NULL, raw_json TEXT NOT NULL,
                    PRIMARY KEY (timestamp_utc, source));
                INSERT INTO readings VALUES ('2026-10-17T02:26:00Z', 'current', NULL, 120, 3, 1, 0, 0, '2026-10-17T02:26:35.000Z', '{"a":1}');
                INSERT INTO readings VALUES ('2026-10-17T02:27:00Z', 'current', NULL, 121, 4, 1, 0, 0, '2026-10-17T02:27:35.000Z', '{"a":2}');
                INSERT INTO readings VALUES ('2026-10-17T01:56:00Z', 'graph', NULL, 110, NULL, 1, 0, 0, '2026-10-17T02:28:00.000Z', '{"a":3}');
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        await _archive.InitialiseAsync(CancellationToken.None);

        var rows = await _archive.ReadAfterAsync(0, 100, TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.Equal([1L, 2L, 3L], rows.Select(r => r.Id));
        Assert.Equal(T0, rows[0].Reading.TimestampUtc);
        Assert.Equal(ReadingSource.Graph, rows[2].Reading.Source);
        Assert.Equal(4, rows[1].Reading.TrendArrow);
        Assert.Null(rows[2].Reading.TrendArrow);
        Assert.Equal("""{"a":3}""", rows[2].Reading.RawJson);
        Assert.Equal(0, await _archive.GetPublishedIdAsync("nightscout", CancellationToken.None));

        // New rows continue after the existing ids.
        await _archive.InsertAsync([Reading(T0.AddMinutes(2), ReadingSource.Current)], CancellationToken.None);
        var next = Assert.Single(await _archive.ReadAfterAsync(3, 100, TimeSpan.FromMinutes(2), CancellationToken.None));
        Assert.Equal(4, next.Id);

        // Insert-or-ignore still works on the new UNIQUE constraint.
        Assert.Equal(0, await _archive.InsertAsync([Reading(T0, ReadingSource.Current, value: 999)], CancellationToken.None));
    }

    [Fact]
    public async Task ReadAfter_is_in_insertion_order_and_flags_graph_points_near_current_readings()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync([Reading(T0, ReadingSource.Current), Reading(T0.AddMinutes(1), ReadingSource.Current)], CancellationToken.None);
        await _archive.InsertAsync(
            [
                Reading(T0.AddSeconds(1), ReadingSource.Graph),      // next to a current reading
                Reading(T0.AddMinutes(-30), ReadingSource.Graph),    // older, in a gap, inserted later
            ],
            CancellationToken.None);

        var rows = await _archive.ReadAfterAsync(0, 100, TimeSpan.FromMinutes(2), CancellationToken.None);

        Assert.Equal([1L, 2L, 3L, 4L], rows.Select(r => r.Id));
        Assert.True(rows[2].HasNearbyBetter);
        Assert.False(rows[3].HasNearbyBetter);

        var afterTwo = await _archive.ReadAfterAsync(2, 1, TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.Equal(3, Assert.Single(afterTwo).Id);
    }

    [Fact]
    public async Task Sensors_are_recorded_once_and_updated()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var activated = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

        Assert.True(await _archive.RecordSensorAsync("SN1", activated, 60, 4, "{\"v\":1}", T0, CancellationToken.None));
        Assert.False(await _archive.RecordSensorAsync("SN1", null, null, null, "{\"v\":2}", T0.AddMinutes(1), CancellationToken.None));

        var sensor = Assert.IsType<ArchivedSensor>(await _archive.GetLatestSensorAsync(CancellationToken.None));
        Assert.Equal("SN1", sensor.Serial);
        Assert.Equal(activated, sensor.ActivatedAtUtc);        // kept when a later poll omits it
        Assert.Equal(60, sensor.WarmupMinutes);
        Assert.Equal(T0, sensor.FirstSeenUtc);
        Assert.Equal(T0.AddMinutes(1), sensor.LastSeenUtc);
        Assert.Equal("{\"v\":2}", sensor.RawJson);

        Assert.True(await _archive.RecordSensorAsync("SN2", activated.AddDays(15), 60, 4, "{}", T0.AddMinutes(2), CancellationToken.None));
        Assert.Equal("SN2", (await _archive.GetLatestSensorAsync(CancellationToken.None))?.Serial);
    }

    [Fact]
    public async Task Recent_sensors_are_most_recently_seen_first()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var activated = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
        await _archive.RecordSensorAsync("SN1", activated, 60, 3, "{}", T0, CancellationToken.None);
        await _archive.RecordSensorAsync("SN2", activated.AddDays(15), 60, 3, "{}", T0.AddMinutes(1), CancellationToken.None);
        await _archive.RecordSensorAsync("SN3", activated.AddDays(30), 60, 3, "{}", T0.AddMinutes(2), CancellationToken.None);

        var recent = await _archive.GetRecentSensorsAsync(2, CancellationToken.None);

        Assert.Equal(["SN3", "SN2"], recent.Select(s => s.Serial));
    }

    [Fact]
    public async Task Readings_keep_their_sensor_serial()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync(
            [Reading(T0, ReadingSource.Current) with { SensorSerial = "SN1" }, Reading(T0.AddMinutes(-30), ReadingSource.Graph)],
            CancellationToken.None);

        var rows = await _archive.ReadAfterAsync(0, 10, TimeSpan.FromMinutes(2), CancellationToken.None);

        Assert.Equal("SN1", rows[0].Reading.SensorSerial);
        Assert.Null(rows[1].Reading.SensorSerial);
    }

    [Fact]
    public async Task Nearby_flag_follows_source_priority()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InsertAsync(
            [
                Reading(T0, ReadingSource.Current),
                Reading(T0.AddSeconds(19), ReadingSource.Graph),                  // near current
                Reading(T0.AddHours(1).AddSeconds(19), ReadingSource.Graph),      // alone
                Reading(T0.AddHours(1), ReadingSource.LibreView),                 // near graph
                Reading(T0.AddHours(2), ReadingSource.LibreView),                 // alone
                Reading(T0.AddHours(2).AddMinutes(1), ReadingSource.LibreViewScan), // near libreview
            ],
            CancellationToken.None);

        var flags = (await _archive.ReadAfterAsync(0, 100, TimeSpan.FromMinutes(2), CancellationToken.None))
            .ToDictionary(r => (r.Reading.Source, r.Reading.TimestampUtc), r => r.HasNearbyBetter);

        Assert.False(flags[(ReadingSource.Current, T0)]);
        Assert.True(flags[(ReadingSource.Graph, T0.AddSeconds(19))]);
        Assert.False(flags[(ReadingSource.Graph, T0.AddHours(1).AddSeconds(19))]);
        Assert.True(flags[(ReadingSource.LibreView, T0.AddHours(1))]);
        Assert.False(flags[(ReadingSource.LibreView, T0.AddHours(2))]);
        Assert.True(flags[(ReadingSource.LibreViewScan, T0.AddHours(2).AddMinutes(1))]);
    }

    [Fact]
    public async Task Events_are_deduplicated_on_source_kind_and_raw_record()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var insulin = new ArchivedEvent(T0, "17-10-2026 03:26", "long_acting_insulin", 14, "units", null, "libreview", T0, "{\"row\":1}");

        Assert.Equal(1, await _archive.InsertEventsAsync([insulin], CancellationToken.None));
        Assert.Equal(0, await _archive.InsertEventsAsync([insulin with { ReceivedAtUtc = T0.AddDays(1) }], CancellationToken.None));
        Assert.Equal(1, await _archive.InsertEventsAsync([insulin with { Kind = "note", Value = null, Text = "x" }], CancellationToken.None));
        Assert.Equal(2, await _archive.CountEventsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Publish_watermark_round_trips_per_target()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        await _archive.SetPublishedIdAsync("nightscout", 5, CancellationToken.None);
        await _archive.SetPublishedIdAsync("nightscout", 7, CancellationToken.None);
        await _archive.SetPublishedIdAsync("other", 2, CancellationToken.None);

        Assert.Equal(7, await _archive.GetPublishedIdAsync("nightscout", CancellationToken.None));
        Assert.Equal(2, await _archive.GetPublishedIdAsync("other", CancellationToken.None));
    }
}
