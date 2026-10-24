using System.Globalization;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries.Tests;

/// <summary>The publisher end to end: a real SQLite archive in, a throwaway SQL Server database out.</summary>
public sealed class SummaryPublisherTests : IAsyncLifetime
{
    private const string Station = "12345678";
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"weather-summaries-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(T0);
    private WeatherArchive _archive = null!;
    private TestSqlServer? _sql;
    private SummaryStore _store = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _archive = new WeatherArchive(Path.Combine(_dir, "weather.db"));
        await _archive.InitialiseAsync(CancellationToken.None);

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

        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [SqlServerFact]
    public async Task Initialise_is_idempotent_and_creates_the_tables()
    {
        await _store.InitialiseAsync(CancellationToken.None);

        Assert.Equal(0, await _store.GetPublishedIdAsync(SummaryPublisher.Target, CancellationToken.None));
        Assert.Empty(await ReadAllAsync());
        Assert.Equal(SummaryStore.SchemaVersion, await ScalarAsync<int>("SELECT Version FROM dbo.SchemaVersion;"));
    }

    [SqlServerFact]
    public async Task Publishes_every_minute_and_moves_the_watermark()
    {
        await AppendTemperatureAsync(0, 10.0);
        await AppendRainAsync(12, 500.0);
        await AppendTemperatureAsync(24, 11.0);
        await AppendRainAsync(60, 500.4);
        await AppendNeighbourAsync(70);

        var written = await Publisher().PublishPendingAsync(CancellationToken.None);

        var minutes = await ReadAllAsync();
        Assert.Equal(2, written);
        Assert.Equal([T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1)], minutes.Select(m => m.MinuteUtc));
        Assert.Equal((3, 10.5, 0.0), (minutes[0].Messages, minutes[0].TemperatureAvgC, minutes[0].RainMm));
        Assert.Equal((1, 0.4), (minutes[1].Messages, minutes[1].RainMm));
        // Up to the station's last reading (id 4); the neighbour's message (id 5) isn't the station's.
        Assert.Equal(4, await _store.GetPublishedIdAsync(SummaryPublisher.Target, CancellationToken.None));
    }

    [SqlServerFact]
    public async Task A_minute_still_filling_at_the_last_run_is_completed_by_the_next()
    {
        await AppendTemperatureAsync(0, 10.0);
        await Publisher().PublishPendingAsync(CancellationToken.None);

        await AppendRainAsync(12, 500.0);
        await AppendTemperatureAsync(24, 12.0);
        await Publisher().PublishPendingAsync(CancellationToken.None);

        var minute = Assert.Single(await ReadAllAsync());
        Assert.Equal((3, 11.0, 10.0, 12.0), (minute.Messages, minute.TemperatureAvgC, minute.TemperatureMinC, minute.TemperatureMaxC));
    }

    [SqlServerFact]
    public async Task Rain_continues_from_the_last_total_published_before()
    {
        await AppendRainAsync(0, 500.0);
        await Publisher().PublishPendingAsync(CancellationToken.None);

        await AppendTemperatureAsync(60, 10.0); // the reading just before the batch carries no rain total
        await Publisher().PublishPendingAsync(CancellationToken.None);
        await AppendRainAsync(120, 501.2);
        await Publisher().PublishPendingAsync(CancellationToken.None);

        Assert.Equal([0.0, null, 1.2], (await ReadAllAsync()).Select(m => m.RainMm));
    }

    [SqlServerFact]
    public async Task Small_batches_give_the_same_minutes_as_one_large_one()
    {
        for (var i = 0; i < 25; i++)
        {
            if (i % 2 == 0)
            {
                await AppendTemperatureAsync(i * 12, 10 + i * 0.1);
            }
            else
            {
                await AppendRainAsync(i * 12, 500 + i * 0.4);
            }
        }

        await Publisher(batchSize: 3).PublishPendingAsync(CancellationToken.None);
        var small = await ReadAllAsync();

        await ExecuteAsync("DELETE dbo.MinuteSummaries; DELETE dbo.PublishState;");
        await Publisher(batchSize: 1000).PublishPendingAsync(CancellationToken.None);

        Assert.Equal(5, small.Count);
        Assert.Equal(await ReadAllAsync(), small);
    }

    [SqlServerFact]
    public async Task An_emptied_store_is_rebuilt_from_the_archive()
    {
        await AppendTemperatureAsync(0, 10.0);
        await Publisher().PublishPendingAsync(CancellationToken.None);
        await ExecuteAsync("DELETE dbo.MinuteSummaries; DELETE dbo.PublishState;");

        await Publisher().PublishPendingAsync(CancellationToken.None);

        Assert.Equal(10.0, Assert.Single(await ReadAllAsync()).TemperatureAvgC);
    }

    [SqlServerFact]
    public async Task Nothing_new_writes_nothing()
    {
        await AppendTemperatureAsync(0, 10.0);
        await Publisher().PublishPendingAsync(CancellationToken.None);

        Assert.Equal(0, await Publisher().PublishPendingAsync(CancellationToken.None));
    }

    private SummaryPublisher Publisher(int batchSize = 5000) => new(
        _archive,
        _store,
        Options.Create(new SummaryOptions { BatchSize = batchSize }),
        _time,
        NullLogger<SummaryPublisher>.Instance);

    private Task<IReadOnlyList<MinuteSummary>> ReadAllAsync() =>
        _store.ReadMinutesAsync(DateTime.MinValue, DateTime.MaxValue, CancellationToken.None);

    private Task AppendTemperatureAsync(double seconds, double temperature) => AppendAsync(
        seconds,
        $$"""{"model" : "Bresser-6in1", "id" : {{Station}}, "battery_ok" : 1, "temperature_C" : {{N(temperature)}}, "humidity" : 85, "wind_max_m_s" : 2.0, "wind_avg_m_s" : 1.0, "wind_dir_deg" : 90, "flags" : 0}""",
        isStation: true);

    private Task AppendRainAsync(double seconds, double total) => AppendAsync(
        seconds,
        $$"""{"model" : "Bresser-6in1", "id" : {{Station}}, "rain_mm" : {{N(total)}}, "wind_max_m_s" : 2.0, "wind_avg_m_s" : 1.0, "wind_dir_deg" : 90, "flags" : 1}""",
        isStation: true);

    private Task AppendNeighbourAsync(double seconds) => AppendAsync(
        seconds, """{"model" : "Bresser-6in1", "id" : 87654321, "temperature_C" : 14.0}""", isStation: false);

    private Task AppendAsync(double seconds, string line, bool isStation) =>
        _archive.AppendAsync(Rtl433Message.Parse(line), T0.AddSeconds(seconds), isStation, CancellationToken.None);

    private static string N(double value) => value.ToString(CultureInfo.InvariantCulture);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_sql!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(_sql!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
