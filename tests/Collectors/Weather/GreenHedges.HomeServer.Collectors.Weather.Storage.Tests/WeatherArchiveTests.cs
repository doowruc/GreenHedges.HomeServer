using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using Microsoft.Data.Sqlite;

namespace GreenHedges.HomeServer.Collectors.Weather.Storage.Tests;

public sealed class WeatherArchiveTests : IDisposable
{
    private const string StationTemperature =
        """{"time" : "1792234567.123456", "protocol" : 172, "model" : "Bresser-6in1", "id" : 12345678, "channel" : 0, "battery_ok" : 0, "temperature_C" : 11.6, "humidity" : 87, "wind_max_m_s" : 1.2, "wind_avg_m_s" : 0.9, "wind_dir_deg" : 112, "startup" : 1, "flags" : 0, "rssi" : -1.5, "snr" : 22.1}""";

    private const string StationRain =
        """{"time" : "1792234579.123456", "protocol" : 172, "model" : "Bresser-6in1", "id" : 12345678, "channel" : 0, "wind_max_m_s" : 2.1, "wind_avg_m_s" : 1.4, "wind_dir_deg" : 45, "rain_mm" : 998.4, "startup" : 1, "flags" : 1, "rssi" : -1.6, "snr" : 21.9}""";

    private const string Neighbour =
        """{"time" : "1792234570.000000", "protocol" : 172, "model" : "Bresser-6in1", "id" : 87654321, "channel" : 0, "temperature_C" : 14.0}""";

    private static readonly DateTimeOffset T0 = new(2026, 10, 18, 9, 0, 0, 123, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"weather-archive-{Guid.NewGuid():N}");
    private readonly WeatherArchive _archive;

    public WeatherArchiveTests()
    {
        Directory.CreateDirectory(_dir);
        _archive = new WeatherArchive(Path.Combine(_dir, "weather.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Initialise_creates_schema_v3_in_wal_mode_and_is_idempotent()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.InitialiseAsync(CancellationToken.None);

        Assert.Equal(3, WeatherArchive.SchemaVersion);
        Assert.Equal("wal", await ScalarAsync("PRAGMA journal_mode;"));
        Assert.Equal(3L, await ScalarAsync("PRAGMA user_version;"));
    }

    [Fact]
    public async Task Station_message_is_archived_raw_and_typed_in_one_go()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        var id = await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0, isStation: true, CancellationToken.None);

        Assert.Equal(StationTemperature, await ScalarAsync($"SELECT raw_json FROM messages WHERE id = {id};"));
        Assert.Equal("2026-10-18T09:00:00.123Z", await ScalarAsync($"SELECT received_at_utc FROM messages WHERE id = {id};"));
        Assert.Equal("1792234567.123456", await ScalarAsync($"SELECT rtl_time FROM messages WHERE id = {id};"));
        Assert.Equal("12345678", await ScalarAsync($"SELECT device_id FROM messages WHERE id = {id};"));

        var reading = await _archive.GetLatestStationReadingAsync(CancellationToken.None);
        Assert.NotNull(reading);
        Assert.Equal(id, reading.MessageId);
        Assert.Equal(T0, reading.ReceivedAtUtc);
        Assert.Equal("12345678", reading.DeviceId);
        Assert.Equal(11.6, reading.Weather.TemperatureC);
        Assert.Equal(87, reading.Weather.Humidity);
        Assert.Equal(0, reading.Weather.BatteryOk);
        Assert.Null(reading.Weather.RainMm);
        Assert.Equal(22.1, reading.Weather.Snr);
    }

    [Fact]
    public async Task Rain_message_keeps_the_running_total()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0, true, CancellationToken.None);
        await _archive.AppendAsync(Rtl433Message.Parse(StationRain), T0.AddSeconds(12), true, CancellationToken.None);

        var reading = (await _archive.GetLatestStationReadingAsync(CancellationToken.None))!;

        Assert.Equal(998.4, reading.Weather.RainMm);
        Assert.Equal(1, reading.Weather.Flags);
        Assert.Null(reading.Weather.TemperatureC);
    }

    [Fact]
    public async Task Other_devices_are_archived_but_get_no_station_reading()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        await _archive.AppendAsync(Rtl433Message.Parse(Neighbour), T0, isStation: false, CancellationToken.None);

        Assert.Equal(1, await _archive.CountMessagesAsync(CancellationToken.None));
        Assert.Null(await _archive.GetLatestStationReadingAsync(CancellationToken.None));
        var device = Assert.Single(await _archive.GetDevicesAsync(CancellationToken.None));
        Assert.Equal(("Bresser-6in1", "87654321", false), (device.Model, device.DeviceId, device.IsStation));
    }

    [Fact]
    public async Task Invalid_lines_are_archived_and_flagged_without_a_device()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        var id = await _archive.AppendAsync(Rtl433Message.Parse("""{"model" : "Bresser"""), T0, isStation: false, CancellationToken.None);

        Assert.Equal(0L, await ScalarAsync($"SELECT is_valid_json FROM messages WHERE id = {id};"));
        Assert.Equal("""{"model" : "Bresser""", await ScalarAsync($"SELECT raw_json FROM messages WHERE id = {id};"));
        Assert.Empty(await _archive.GetDevicesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Devices_count_messages_and_track_first_and_last_seen()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0, true, CancellationToken.None);
        await _archive.AppendAsync(Rtl433Message.Parse(Neighbour), T0.AddSeconds(5), false, CancellationToken.None);
        await _archive.AppendAsync(Rtl433Message.Parse(StationRain), T0.AddSeconds(12), true, CancellationToken.None);

        var devices = await _archive.GetDevicesAsync(CancellationToken.None);

        Assert.Equal(["12345678", "87654321"], devices.Select(d => d.DeviceId));
        var station = devices[0];
        Assert.Equal(2, station.MessageCount);
        Assert.Equal(T0, station.FirstSeenUtc);
        Assert.Equal(T0.AddSeconds(12), station.LastSeenUtc);
        Assert.True(station.IsStation);
    }

    [Fact]
    public async Task Ids_increase_in_arrival_order()
    {
        await _archive.InitialiseAsync(CancellationToken.None);

        var first = await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0, true, CancellationToken.None);
        var second = await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0, true, CancellationToken.None);

        Assert.True(second > first);
        Assert.Equal(2, await _archive.CountMessagesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Backup_is_a_complete_copy()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0, true, CancellationToken.None);
        var backupPath = Path.Combine(_dir, "backup.db");

        await _archive.BackupToAsync(backupPath, CancellationToken.None);

        var copy = new WeatherArchive(backupPath);
        Assert.Equal(1, await copy.CountMessagesAsync(CancellationToken.None));
        Assert.NotNull(await copy.GetLatestStationReadingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Station_readings_are_read_by_id_and_by_time_and_the_neighbours_are_not()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var t1 = await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0, true, CancellationToken.None);
        await _archive.AppendAsync(Rtl433Message.Parse(Neighbour), T0.AddSeconds(5), false, CancellationToken.None);
        var r1 = await _archive.AppendAsync(Rtl433Message.Parse(StationRain), T0.AddSeconds(12), true, CancellationToken.None);
        var t2 = await _archive.AppendAsync(Rtl433Message.Parse(StationTemperature), T0.AddSeconds(24), true, CancellationToken.None);

        Assert.Equal([t1, r1, t2], (await _archive.ReadStationReadingsAfterAsync(0, 10, CancellationToken.None)).Select(r => r.MessageId));
        Assert.Equal([r1], (await _archive.ReadStationReadingsAfterAsync(t1, 1, CancellationToken.None)).Select(r => r.MessageId));
        Assert.Equal(
            [r1],
            (await _archive.ReadStationReadingsFromAsync(T0.AddSeconds(12), r1, CancellationToken.None)).Select(r => r.MessageId));

        var before = await _archive.GetLatestStationReadingBeforeAsync(T0.AddSeconds(24), withRain: false, CancellationToken.None);
        var rainBefore = await _archive.GetLatestStationReadingBeforeAsync(T0.AddSeconds(30), withRain: true, CancellationToken.None);
        Assert.Equal(r1, before?.MessageId);
        Assert.Equal((r1, 998.4), (rainBefore?.MessageId, rainBefore?.Weather.RainMm));
        Assert.Null(await _archive.GetLatestStationReadingBeforeAsync(T0, withRain: false, CancellationToken.None));
    }

    [Fact]
    public async Task A_weather_underground_day_is_stored_whole_and_replaced_by_a_later_fetch()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var day = new DateOnly(2026, 10, 18);
        var first = WundergroundObservation(1792195494, 0.0);
        await _archive.SaveWundergroundDayAsync(day, T0, "{\"observations\":[1]}", [first], CancellationToken.None);
        await _archive.SaveWundergroundDayAsync(new DateOnly(2026, 10, 17), T0.AddSeconds(3), "", [], CancellationToken.None);

        var later = T0.AddDays(1);
        await _archive.SaveWundergroundDayAsync(day, later, "{\"observations\":[1,2]}", [first with { PrecipTotalMm = 0.4 }, WundergroundObservation(1792195794, 0.8)], CancellationToken.None);

        var fetched = await _archive.GetWundergroundDaysAsync(CancellationToken.None);
        Assert.Equal(later, fetched[day]);
        Assert.Equal(T0.AddSeconds(3), fetched[new DateOnly(2026, 10, 17)]);
        Assert.Equal("{\"observations\":[1,2]}", await ScalarAsync("SELECT raw_json FROM wunderground_days WHERE local_date = '2026-10-18';"));
        Assert.Equal(2L, await ScalarAsync("SELECT observation_count FROM wunderground_days WHERE local_date = '2026-10-18';"));

        var days = await _archive.ReadWundergroundDaysFetchedAfterAsync(T0.AddSeconds(1), 10, CancellationToken.None);
        Assert.Equal([new DateOnly(2026, 10, 17), day], days.Select(d => d.LocalDate));
        Assert.Empty(days[0].Observations);
        Assert.Equal([0.4, 0.8], days[1].Observations.Select(o => o.PrecipTotalMm));
        Assert.Equal(first with { PrecipTotalMm = 0.4 }, days[1].Observations[0]);
    }

    private static WundergroundObservation WundergroundObservation(long epoch, double rain) => new(
        epoch, DateTimeOffset.FromUnixTimeSeconds(epoch), 10.5, 11.0, 10.0, 90, 92, 88, 8.9, 7.2, 9.0, 14.4, 225,
        1012.4, 1012.0, 0.0, rain, 1);

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir, "weather.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
