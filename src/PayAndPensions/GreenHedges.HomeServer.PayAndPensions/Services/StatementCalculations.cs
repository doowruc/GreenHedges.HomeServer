namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>One statement's value on its date: what <see cref="StatementCalculations"/> works from.</summary>
public sealed record StatementValue(DateOnly Date, decimal Value);

/// <summary>
/// A statement's worked-out figures, as the pensions spreadsheet had them. Null where there's nothing to work from: no
/// statement before it (and no start date), or a previous value of 0 for the growth.
/// </summary>
/// <param name="Change">The value less the previous statement's (or less 0 from the pension's start).</param>
/// <param name="Growth">The change as a fraction of the previous value.</param>
/// <param name="BelowHighest">The value less the highest of all the type's statements: 0 for the highest.</param>
/// <param name="DaysSinceStart">Days from the pension's start date.</param>
/// <param name="PerDaySinceStart">The value ÷ the days since the start.</param>
/// <param name="PeriodDays">Days since the previous statement (or the start).</param>
/// <param name="ChangePerDay">The change ÷ the period's days.</param>
public sealed record StatementFigures(decimal? Change, decimal? Growth, decimal BelowHighest, int? DaysSinceStart,
    decimal? PerDaySinceStart, int? PeriodDays, decimal? ChangePerDay);

/// <summary>Works out each statement's figures, from one statement type's statements. Pure, so unit tested.</summary>
public static class StatementCalculations
{
    /// <summary>
    /// The figures for <paramref name="statements"/> (one type's, oldest first), in the same order. With a
    /// <paramref name="start"/> date, the first statement's change is from 0 on that date, as the spreadsheet's first row.
    /// </summary>
    public static List<StatementFigures> Calculate(DateOnly? start, IReadOnlyList<StatementValue> statements)
    {
        if (statements.Count == 0) return [];
        var highest = statements.Max(s => s.Value);
        StatementValue? previous = start is { } from ? new StatementValue(from, 0) : null;
        var figures = new List<StatementFigures>(statements.Count);
        foreach (var s in statements)
        {
            decimal? change = previous is null ? null : s.Value - previous.Value;
            int? periodDays = previous is null ? null : s.Date.DayNumber - previous.Date.DayNumber;
            int? sinceStart = start is { } begun ? s.Date.DayNumber - begun.DayNumber : null;
            figures.Add(new StatementFigures(
                change,
                previous is { Value: not 0 } p ? change / p.Value : null,
                s.Value - highest,
                sinceStart,
                sinceStart is > 0 ? s.Value / sinceStart : null,
                periodDays,
                periodDays is > 0 ? change / periodDays : null));
            previous = s;
        }
        return figures;
    }
}
