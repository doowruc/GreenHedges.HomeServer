using System.IO.Compression;
using System.Text;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Weather.History.Tests;

public sealed class WeathercloudTests : IAsyncLifetime
{
    // The yearly zips' short headers, with the trailing ';' the files have.
    private const string ShortHeader =
        "Date (Europe/London);tempin (°C);temp (°C);chill (°C);dewin (°C);dew (°C);heatin (°C);heat (°C);humin (%);hum (%);wspdhi (mph);wspdavg (mph);wdiravg (°);bar (mbar);rain (mm);et (mm);rainrate (mm/h);solarrad (W/m²);uvi;";

    // A single month's download: long headers.
    private const string LongHeader =
        "Date (Europe/London);Inside temperature (°C);Temperature (°C);Wind chill (°C);Inside dew point (°C);Dew point (°C);Inside heat index (°C);Heat index (°C);Inside humidity (%);Humidity (%);Wind gust (mph);Average wind speed (mph);Average wind direction (°);Atmospheric pressure (mbar);Rain (mm);Evapotranspiration (mm);Rain rate (mm/h);Solar radiation (W/m²);UV index;";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"weather-wc-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
    private WeatherArchive _archive = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _archive = new WeatherArchive(Path.Combine(_dir, "weather.db"));
        await _archive.InitialiseAsync(CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(ShortHeader)]
    [InlineData(LongHeader)]
    public void Reads_utf16_without_a_byte_order_mark_with_either_header_style_converting_units(string header)
    {
        var result = WeathercloudParser.Parse(Export(header,
            "01/09/2026 00:00:00;22.7;16.5;16.5;;11.9;;;57;74;1.1;1.1;202;1,007.9;0;;0;;;",
            "01/09/2026 00:10:00;;;;;;;;;;;;;;;;;;;",
            "01/09/2026 00:20:00;22.7;16;16;;11.7;;;57;76;0;0;202;1,007.7;0.4;;0;;;"));

        Assert.Equal("Europe/London", result.TimeZone);
        Assert.Equal((3, 1, 0, 0), (result.RowsRead, result.SkippedEmpty, result.SkippedInvalidTime, result.SkippedUnparseable));
        var row = result.Rows[0];
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 23, 0, 0, TimeSpan.Zero), row.TimeUtc); // 00:00 BST
        Assert.Equal("01/09/2026 00:00:00", row.LocalTime);
        Assert.StartsWith("01/09/2026 00:00:00;22.7;16.5", row.RawLine);
        Assert.Equal((22.7, 16.5, 11.9, 57.0, 74.0), (row.IndoorTemperatureC, row.TemperatureC, row.DewPointC, row.IndoorHumidity, row.Humidity));
        Assert.Equal(0.492, row.GustMs);   // 1.1 mph
        Assert.Equal((202.0, 1007.9, 0.0), (row.WindDirDeg, row.PressureHpa, row.RainDayMm));
        Assert.Equal(0.4, result.Rows[1].RainDayMm);
    }

    [Fact]
    public void The_repeated_autumn_hour_is_summer_time_then_winter_time_in_file_order()
    {
        var lines = new[] { "00:50", "01:00", "01:50", "01:00", "01:50", "02:00" }
            .Select(t => $"26/10/2025 {t}:00;15.5;6;6;;5;;;50;90;0;0;180;1,010;0;;0;;;").ToArray();

        var times = WeathercloudParser.Parse(Export(ShortHeader, lines)).Rows.Select(r => r.TimeUtc.UtcDateTime.ToString("HH:mm"));

        Assert.Equal(["23:50", "00:00", "00:50", "01:00", "01:50", "02:00"], times);
    }

    [Fact]
    public void A_time_in_the_skipped_spring_hour_is_counted_and_skipped()
    {
        var result = WeathercloudParser.Parse(Export(ShortHeader,
            "30/03/2025 00:50:00;17.6;10;10;;5;;;50;90;0;0;180;1,010;0;;0;;;",
            "30/03/2025 01:30:00;17.6;10;10;;5;;;50;90;0;0;180;1,010;0;;0;;;",
            "30/03/2025 02:00:00;17.6;10;10;;5;;;50;90;0;0;180;1,010;0;;0;;;"));

        Assert.Equal(1, result.SkippedInvalidTime);
        Assert.Equal(["00:50", "01:00"], result.Rows.Select(r => r.TimeUtc.UtcDateTime.ToString("HH:mm")));
    }

    [Fact]
    public void Something_else_is_refused()
    {
        Assert.Throws<FormatException>(() => WeathercloudParser.Parse(Export("Time;Glucose", "01/09/2026 00:00;5.4")));
    }

    [Fact]
    public async Task Imports_zips_and_files_by_local_day_and_a_later_export_replaces_a_day()
    {
        var zip = Path.Combine(_dir, "Weathercloud_Bresser_2026.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            await using var entry = archive.CreateEntry("Weathercloud Bresser 2026-09.csv").Open();
            Export(ShortHeader,
                "30/09/2026 23:50:00;22;15;15;;10;;;50;80;0;0;180;1,004.2;2.4;;0;;;").CopyTo(entry);
        }

        var partial = Path.Combine(_dir, "Weathercloud Bresser 2026-10.csv");
        await File.WriteAllBytesAsync(partial, Export(LongHeader,
            "01/10/2026 00:00:00;22;15;15;;10;;;50;80;0;0;180;1,004.0;0;;0;;;").ToArray());

        var results = await Importer().ImportAsync([zip, partial], CancellationToken.None);
        Assert.Equal(["Weathercloud_Bresser_2026.zip/Weathercloud Bresser 2026-09.csv", "Weathercloud Bresser 2026-10.csv"], results.Select(r => r.File));

        // A fuller export of October, later: 1 October is replaced, with a new version.
        await File.WriteAllBytesAsync(partial, Export(LongHeader,
            "01/10/2026 00:00:00;22;15;15;;10;;;50;80;0;0;180;1,004.0;0;;0;;;",
            "01/10/2026 00:10:00;22;15;15;;10;;;50;80;0;0;180;1,004.0;0.2;;0;;;").ToArray());
        await Importer().ImportAsync([_dir], CancellationToken.None);

        var days = await _archive.ReadWeathercloudDaysAfterAsync(0, 10, CancellationToken.None);
        Assert.Equal([new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1)], days.Select(d => d.LocalDate).Order());
        var october = days.Single(d => d.LocalDate == new DateOnly(2026, 10, 1));
        Assert.Equal([0.0, 0.2], october.Rows.Select(r => r.RainDayMm));
        Assert.Equal(2, (await _archive.ReadWeathercloudDaysAfterAsync(october.Version - 1, 10, CancellationToken.None)).Count(d => d.Version >= october.Version));
    }

    private WeathercloudImporter Importer() => new(_archive, _time, NullLogger<WeathercloudImporter>.Instance);

    /// <summary>An export as Weathercloud writes it: UTF-16LE, no byte-order mark, CRLF line ends.</summary>
    private static MemoryStream Export(string header, params string[] rows) =>
        new(Encoding.Unicode.GetBytes(string.Join("\r\n", [header, .. rows]) + "\r\n"));
}
