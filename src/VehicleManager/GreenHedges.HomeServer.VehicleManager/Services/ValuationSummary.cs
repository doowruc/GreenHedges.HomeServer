using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// A vehicle's values for the pre-renewal page, by type and grade: each source's figure from the latest round of
/// lookups, their average, and the previous round's average for the same type and grade. A round is the values within
/// <see cref="RoundDays"/> days up to its newest one; the previous round is the same up to the newest value before it.
/// </summary>
public sealed record ValuationSummary(DateOnly From, DateOnly To, IReadOnlyList<ValuationSource> Sources, IReadOnlyList<ValuationSummary.Row> Rows,
    DateOnly? PreviousFrom, DateOnly? PreviousTo)
{
    public const int RoundDays = 30;

    /// <summary>One type and grade: each source's latest value in the round, and the average of them.</summary>
    public sealed record Row(ValuationType Type, ValuationGrade? Grade, IReadOnlyDictionary<ValuationSource, VehicleValuationView> BySource,
        decimal Average, decimal? PreviousAverage)
    {
        public string Label => Grade is { } grade ? $"{VehicleValuationView.TypeNames(Type)} ({grade})" : VehicleValuationView.TypeNames(Type);
        public decimal? Change => PreviousAverage is { } previous ? Average - previous : null;
    }

    /// <returns>Null without any values.</returns>
    public static ValuationSummary? For(IEnumerable<VehicleValuationView> values)
    {
        var all = values.ToList();
        var latest = Round(all);
        if (latest is null) return null;
        var previous = Round(all.Where(v => v.Date < latest.Value.From));

        var rows = Averages(latest.Value.Values)
            .Select(r => r with
            {
                PreviousAverage = previous is null ? null
                    : Averages(previous.Value.Values).FirstOrDefault(p => p.Type == r.Type && p.Grade == r.Grade)?.Average
            })
            .ToList();
        var sources = latest.Value.Values.Select(v => v.Source).Distinct().Order().ToList();
        return new(latest.Value.From, latest.Value.To, sources, rows, previous?.From, previous?.To);
    }

    /// <summary>The values within <see cref="RoundDays"/> days up to the newest of <paramref name="values"/>, and the dates they span.</summary>
    private static (DateOnly From, DateOnly To, List<VehicleValuationView> Values)? Round(IEnumerable<VehicleValuationView> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return null;
        var to = list.Max(v => v.Date);
        var inRound = list.Where(v => v.Date > to.AddDays(-RoundDays)).ToList();
        return (inRound.Min(v => v.Date), to, inRound);
    }

    /// <summary>Each type and grade (in order: type, then no grade, poor, average, good), with each source's latest value and their average.</summary>
    private static IEnumerable<Row> Averages(IEnumerable<VehicleValuationView> round) =>
        round.GroupBy(v => (v.Type, v.Grade))
            .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.Grade.HasValue).ThenBy(g => g.Key.Grade)
            .Select(g =>
            {
                var bySource = g.GroupBy(v => v.Source)
                    .ToDictionary(s => s.Key, s => s.OrderByDescending(v => v.Date).ThenByDescending(v => v.Id).First());
                return new Row(g.Key.Type, g.Key.Grade, bySource, bySource.Values.Average(v => v.Value), null);
            });
}
