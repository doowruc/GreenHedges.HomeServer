using System.IO.Compression;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Extensions.Logging;

namespace GreenHedges.HomeServer.Collectors.Weather.History;

/// <summary>What importing one export (a CSV, or a CSV inside a zip) did.</summary>
public sealed record WeathercloudFileResult(
    string File,
    int RowsRead,
    int RowsImported,
    int Days,
    int SkippedEmpty,
    int SkippedInvalidTime,
    int SkippedUnparseable,
    DateTimeOffset? FirstUtc,
    DateTimeOffset? LastUtc);

/// <summary>
/// Imports Weathercloud exports into the archive: CSV files, zips of them (a year's download), or folders of either.
/// Each local day in a file replaces that day's earlier import, so it's safe to re-run, and a later, fuller export of a
/// month completes it. Days with no data are left out.
/// </summary>
public sealed class WeathercloudImporter(WeatherArchive archive, TimeProvider time, ILogger<WeathercloudImporter> logger)
{
    public async Task<IReadOnlyList<WeathercloudFileResult>> ImportAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var results = new List<WeathercloudFileResult>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.EnumerateFiles(path)
                    .Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal);
                results.AddRange(await ImportAsync(files, cancellationToken));
            }
            else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName, StringComparer.Ordinal))
                {
                    await using var stream = entry.Open();
                    results.Add(await ImportFileAsync($"{Path.GetFileName(path)}/{entry.FullName}", stream, cancellationToken));
                }
            }
            else
            {
                await using var stream = File.OpenRead(path);
                results.Add(await ImportFileAsync(Path.GetFileName(path), stream, cancellationToken));
            }
        }

        return results;
    }

    private async Task<WeathercloudFileResult> ImportFileAsync(string name, Stream stream, CancellationToken cancellationToken)
    {
        var parsed = WeathercloudParser.Parse(stream);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(parsed.TimeZone);
        var days = parsed.Rows
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.TimeUtc, zone).DateTime))
            .Select(g => new WeathercloudDay(g.Key, g.OrderBy(r => r.TimeUtc).ToList()))
            .ToList();

        await archive.SaveWeathercloudDaysAsync(days, name, time.GetUtcNow(), cancellationToken);
        logger.LogInformation("Weathercloud: {File}: {Rows:N0} rows in {Days} days", name, parsed.Rows.Count, days.Count);

        return new WeathercloudFileResult(
            name, parsed.RowsRead, parsed.Rows.Count, days.Count, parsed.SkippedEmpty, parsed.SkippedInvalidTime, parsed.SkippedUnparseable,
            parsed.Rows.Count > 0 ? parsed.Rows.Min(r => r.TimeUtc) : null,
            parsed.Rows.Count > 0 ? parsed.Rows.Max(r => r.TimeUtc) : null);
    }
}
