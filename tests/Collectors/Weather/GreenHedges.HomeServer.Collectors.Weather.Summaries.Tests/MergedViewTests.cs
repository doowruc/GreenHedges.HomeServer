using Microsoft.Data.SqlClient;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries.Tests;

/// <summary>The views the dashboards read: each hour from its best source.</summary>
public sealed class MergedViewTests : IAsyncLifetime
{
    private static readonly DateTime H0 = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 4, 30, 0, TimeSpan.Zero);

    private TestSqlServer? _sql;
    private SummaryStore _store = null!;

    public async Task InitializeAsync()
    {
        if (TestSqlServer.IsConfigured)
        {
            _sql = await TestSqlServer.CreateAsync();
            _store = new SummaryStore(_sql.ConnectionString);
            await _store.InitialiseAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task Outdoor_takes_each_hour_from_the_radio_then_weather_underground_then_weathercloud()
    {
        // Hour 10: all three. Hour 11: Weather Underground and Weathercloud. Hour 12: Weathercloud only.
        await _store.WriteAsync([Minute(H0.AddMinutes(30), 10.0)], "minutes-v1", 1, Now, CancellationToken.None);
        await History(HistoryPublisher.WundergroundSource, [Obs(H0.AddMinutes(35), 20.0), Obs(H0.AddHours(1).AddMinutes(5), 21.0)]);
        await History(HistoryPublisher.WeathercloudSource, [Obs(H0.AddMinutes(40), 30.0), Obs(H0.AddHours(1).AddMinutes(10), 31.0), Obs(H0.AddHours(2).AddMinutes(10), 32.0)]);

        var rows = await QueryAsync("SELECT Source, TemperatureAvgC, TemperatureMinC FROM dbo.Outdoor ORDER BY TimeUtc");

        Assert.Equal([("radio", 10.0, 10.0), ("wunderground", 21.0, 21.0), ("weathercloud", 32.0, 32.0)], rows);
    }

    [SqlServerFact]
    public async Task The_radios_dew_point_is_calculated_and_pressure_and_indoor_come_from_the_history()
    {
        await _store.WriteAsync([Minute(H0, 10.0)], "minutes-v1", 1, Now, CancellationToken.None);
        await History(HistoryPublisher.WundergroundSource, [Obs(H0.AddMinutes(5), 10.0, pressure: 1001.0)]);
        await History(HistoryPublisher.WeathercloudSource, [Obs(H0.AddMinutes(10), 10.0, pressure: 1003.0, indoor: 21.5), Obs(H0.AddHours(1), 10.0, pressure: 1004.0, indoor: 21.0)]);

        Assert.Equal(8.4, await ScalarAsync("SELECT ROUND(DewPointC, 1) FROM dbo.Outdoor WHERE Source = 'radio'")); // 10 °C, 90 %
        Assert.Equal([1001.0, 1004.0], (await QueryAsync("SELECT Source, PressureHpa, PressureHpa FROM dbo.Pressure ORDER BY TimeUtc")).Select(r => r.Item2));
        Assert.Equal([21.5, 21.0], (await QueryAsync("SELECT 'x', TemperatureC, Humidity FROM dbo.Indoor ORDER BY TimeUtc")).Select(r => r.Item2));
    }

    [SqlServerFact]
    public async Task Hourly_follows_each_write_with_the_same_source_order()
    {
        await History(HistoryPublisher.WeathercloudSource, [Obs(H0.AddMinutes(10), 30.0, pressure: 1003.0, indoor: 21.0), Obs(H0.AddMinutes(20), 31.0, pressure: 1005.0, indoor: 22.0), Obs(H0.AddHours(1).AddMinutes(10), 32.0)]);
        var before = await HourlyAsync();
        Assert.Equal([("weathercloud", 30.5, 2), ("weathercloud", 32.0, 1)], before.Select(h => (h.Source, h.Temperature, h.Rows)));
        Assert.Equal((1004.0, 1003.0, 1005.0, 21.5), (before[0].Pressure, before[0].PressureMin, before[0].PressureMax, before[0].Indoor));

        // The radio arrives for the first hour: it takes over that hour's outdoor readings; pressure and indoor stay.
        await _store.WriteAsync([Minute(H0.AddMinutes(30), 10.0), Minute(H0.AddMinutes(31), 12.0)], "minutes-v1", 1, Now, CancellationToken.None);
        var after = await HourlyAsync();
        Assert.Equal([("radio", 11.0, 2), ("weathercloud", 32.0, 1)], after.Select(h => (h.Source, h.Temperature, h.Rows)));
        Assert.Equal((1004.0, 21.5), (after[0].Pressure, after[0].Indoor));
    }

    [SqlServerFact]
    public async Task Upgrading_to_the_hourly_table_fills_it_from_the_data_already_there()
    {
        await _store.WriteAsync([Minute(H0, 10.0)], "minutes-v1", 1, Now, CancellationToken.None);
        await History(HistoryPublisher.WeathercloudSource, [Obs(H0.AddHours(-3), 5.0)]);
        await ExecuteAsync("DROP TABLE dbo.Hourly; UPDATE dbo.SchemaVersion SET Version = 3;");

        await _store.InitialiseAsync(CancellationToken.None);

        Assert.Equal([("weathercloud", 5.0, 1), ("radio", 10.0, 1)], (await HourlyAsync()).Select(h => (h.Source, h.Temperature, h.Rows)));
    }

    private async Task<List<(string Source, double Temperature, int Rows, double Pressure, double PressureMin, double PressureMax, double Indoor)>> HourlyAsync()
    {
        await using var connection = new SqlConnection(_sql!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Source, TemperatureAvgC, OutdoorRows, PressureHpa, PressureMinHpa, PressureMaxHpa, IndoorTemperatureC FROM dbo.Hourly ORDER BY HourUtc", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, double, int, double, double, double, double)>();
        while (await reader.ReadAsync())
        {
            double D(int i) => reader.IsDBNull(i) ? double.NaN : reader.GetDouble(i);
            rows.Add((reader.GetString(0), D(1), reader.GetInt32(2), D(3), D(4), D(5), D(6)));
        }

        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_sql!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private Task History(string source, IReadOnlyList<HistoryObservation> rows) =>
        _store.ReplaceHistoryAsync(source, DateTime.MinValue, DateTime.MaxValue, rows, source + "-test", 1, Now, CancellationToken.None);

    private static MinuteSummary Minute(DateTime time, double temperature) =>
        new(time, 5, temperature, temperature, temperature, 90, 1, 2, 90, 0, true, -1, 20);

    private static HistoryObservation Obs(DateTime time, double temperature, double? pressure = null, double? indoor = null) =>
        new(time, 5, temperature, null, null, 90, 8, 1, 2, 90, 0, pressure, indoor, indoor is null ? null : 50);

    private async Task<List<(string, double, double)>> QueryAsync(string sql)
    {
        await using var connection = new SqlConnection(_sql!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, double, double)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetDouble(1), reader.IsDBNull(2) ? double.NaN : reader.GetDouble(2)));
        }

        return rows;
    }

    private async Task<double> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(_sql!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (double)(await command.ExecuteScalarAsync())!;
    }
}
