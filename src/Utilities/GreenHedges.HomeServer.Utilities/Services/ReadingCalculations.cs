namespace GreenHedges.HomeServer.Utilities.Services;

/// <param name="InstalledOn">Null for a utility's first meter, whose start isn't known.</param>
/// <param name="StartReading">What it showed when installed; null for a utility's first meter.</param>
public sealed record MeterInfo(int Id, DateOnly? InstalledOn, decimal? StartReading);

public sealed record ReadingInfo(int Id, int MeterId, DateOnly Date, decimal Value);

/// <summary>A reading with its worked-out figures.</summary>
/// <param name="Units">Used since the reading before on the same meter (a replacement's first: since its start reading). Null where counting only begins.</param>
/// <param name="Days">Since the utility's reading before, whichever meter that was on. Null for the first.</param>
/// <param name="UnitsPerDay"><paramref name="Units"/> ÷ <paramref name="Days"/>.</param>
/// <param name="NewMeter">The first reading on a replacement meter.</param>
public sealed record ReadingRow(int Id, int MeterId, DateOnly Date, decimal Value, decimal? Units, int? Days, decimal? UnitsPerDay, bool NewMeter);

/// <summary>The figures the spreadsheet worked out for each reading: units used, days and units a day. Pure.</summary>
public static class ReadingCalculations
{
    /// <summary>One utility's readings, over all its meters, oldest first.</summary>
    public static List<ReadingRow> Calculate(IEnumerable<MeterInfo> meters, IEnumerable<ReadingInfo> readings)
    {
        var meterById = meters.ToDictionary(m => m.Id);
        var lastOnMeter = new Dictionary<int, decimal>();
        DateOnly? previousDate = null;
        var rows = new List<ReadingRow>();

        foreach (var reading in readings.OrderBy(r => r.Date).ThenBy(r => meterById[r.MeterId].InstalledOn))
        {
            var meter = meterById[reading.MeterId];
            var first = !lastOnMeter.TryGetValue(reading.MeterId, out var before);
            var units = first
                ? meter.StartReading is { } start ? reading.Value - start : (decimal?)null
                : reading.Value - before;
            var days = previousDate is { } previous ? reading.Date.DayNumber - previous.DayNumber : (int?)null;
            var perDay = units is { } u && days is > 0 ? u / days.Value : (decimal?)null;

            rows.Add(new ReadingRow(reading.Id, reading.MeterId, reading.Date, reading.Value, units, days, perDay,
                NewMeter: first && meter.StartReading is not null));
            lastOnMeter[reading.MeterId] = reading.Value;
            previousDate = reading.Date;
        }

        return rows;
    }

    /// <summary>
    /// The meter a reading on <paramref name="date"/> belongs to: the latest installed on or before it (a utility's
    /// first meter, with no installed date, counts as always there). Null if every meter was installed later.
    /// </summary>
    public static MeterInfo? MeterFor(IEnumerable<MeterInfo> meters, DateOnly date) =>
        meters.Where(m => m.InstalledOn is null || m.InstalledOn <= date)
            .OrderByDescending(m => m.InstalledOn ?? DateOnly.MinValue)
            .FirstOrDefault();
}
