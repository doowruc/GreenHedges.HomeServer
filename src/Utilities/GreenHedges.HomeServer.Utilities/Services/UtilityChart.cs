using System.Text.Json;

namespace GreenHedges.HomeServer.Utilities.Services;

/// <summary>The data for a utility's chart (wwwroot/js/utility-chart.js), as JSON for a canvas's data attribute.</summary>
public static class UtilityChart
{
    /// <summary>The trendline's order, as the spreadsheet's chart.</summary>
    public const int TrendOrder = 4;

    /// <summary>How many points the trendline is drawn through: enough to look like a curve.</summary>
    private const int TrendSamples = 120;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Units a day at each reading's date (water in litres), oldest first, and a polynomial trendline fitted against
    /// the date. Null with fewer than two daily figures: nothing to draw. The trend is empty with fewer than three.
    /// </summary>
    public static string? For(UtilityHistory history)
    {
        var scale = history.Utility.PerDayUnit(history.Unit) == "litres" ? 1000m : 1m;
        var points = history.Rows.Where(r => r.UnitsPerDay is not null)
            .Select(r => (r.Date, Value: r.UnitsPerDay!.Value * scale)).ToList();
        if (points.Count < 2) return null;

        var trend = PolynomialTrend.Fit(points.Select(p => ((double)p.Date.DayNumber, (double)p.Value)).ToList(), TrendOrder);
        var (first, last) = (points[0].Date.DayNumber, points[^1].Date.DayNumber);
        var samples = trend is null ? [] : Enumerable.Range(0, TrendSamples + 1)
            .Select(i => first + (last - first) * (double)i / TrendSamples)
            .Select(day => new { date = DateOnly.FromDayNumber((int)Math.Round(day)), value = Math.Round(trend.At(Math.Round(day)), 3) })
            .DistinctBy(s => s.date)
            .ToList();

        return JsonSerializer.Serialize(new
        {
            unit = history.Utility.PerDayUnit(history.Unit),
            decimals = scale == 1000m ? 0 : 2,
            points = points.Select(p => new { date = p.Date, value = Math.Round(p.Value, 3) }),
            trendOrder = trend?.Order,
            trend = samples
        }, Json);
    }
}
