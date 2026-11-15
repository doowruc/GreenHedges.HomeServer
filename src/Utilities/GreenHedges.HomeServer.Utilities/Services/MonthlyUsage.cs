namespace GreenHedges.HomeServer.Utilities.Services;

/// <summary>A calendar year's units a day for each month, January first; null for a month that can't be worked out.</summary>
public sealed record YearUsage(int Year, IReadOnlyList<decimal?> PerDay);

/// <summary>
/// Units a day for each calendar month, to follow each month across the years: for a seasonal utility. Pure.
/// </summary>
public static class MonthlyUsage
{
    /// <summary>A reading stands for the 1st of a month only if it's within this many days of it.</summary>
    public const int NearFirst = 10;

    /// <summary>
    /// A month runs from the reading closest to its 1st to the reading closest to the next month's 1st: the units
    /// used between the two (over a meter change too), divided by the days between them. Readings are rarely on the
    /// 1st itself, so a month's figure is for about that month. A month has none when either end has no reading
    /// within <see cref="NearFirst"/> days, both ends are the same reading, or the units in between aren't all known.
    /// </summary>
    /// <param name="rows">A utility's readings worked out, oldest first (<see cref="ReadingCalculations.Calculate"/>).</param>
    /// <returns>Each year with at least one month's figure, oldest first.</returns>
    public static List<YearUsage> ByMonth(IReadOnlyList<ReadingRow> rows, int nearFirst = NearFirst)
    {
        var years = new List<YearUsage>();
        if (rows.Count < 2) return years;

        for (var year = rows[0].Date.Year - 1; year <= rows[^1].Date.Year; year++)
        {
            var perDay = new decimal?[12];
            for (var month = 1; month <= 12; month++)
            {
                var first = new DateOnly(year, month, 1);
                perDay[month - 1] = Between(rows, ClosestTo(rows, first, nearFirst), ClosestTo(rows, first.AddMonths(1), nearFirst));
            }
            if (perDay.Any(v => v is not null)) years.Add(new YearUsage(year, perDay));
        }
        return years;
    }

    /// <summary>The index of the reading closest to <paramref name="date"/> (the earlier of two as close); null if none is near enough.</summary>
    private static int? ClosestTo(IReadOnlyList<ReadingRow> rows, DateOnly date, int nearFirst)
    {
        int? closest = null;
        var distance = nearFirst + 1;
        for (var i = 0; i < rows.Count; i++)
        {
            var d = Math.Abs(rows[i].Date.DayNumber - date.DayNumber);
            if (d < distance) (closest, distance) = (i, d);
        }
        return closest;
    }

    /// <summary>Units a day from one reading to a later one: each reading's units since the one before it, added up.</summary>
    private static decimal? Between(IReadOnlyList<ReadingRow> rows, int? from, int? to)
    {
        if (from is not { } start || to is not { } end || end <= start) return null;
        var units = 0m;
        for (var i = start + 1; i <= end; i++)
        {
            if (rows[i].Units is not { } used) return null;
            units += used;
        }
        var days = rows[end].Date.DayNumber - rows[start].Date.DayNumber;
        return days > 0 ? units / days : null;
    }
}
