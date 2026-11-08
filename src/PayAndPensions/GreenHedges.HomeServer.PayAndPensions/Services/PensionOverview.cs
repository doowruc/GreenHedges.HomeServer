using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>The figures from one statement that the dashboard uses.</summary>
public sealed record StatementSummary(DateOnly Date, decimal Value, decimal? ProjectedFund = null, decimal? ProjectedAnnualIncome = null);

/// <summary>A pension's value on a date.</summary>
public sealed record PensionValuePoint(DateOnly Date, decimal Value);

/// <summary>What a statement projects a pension to be at retirement.</summary>
public sealed record PensionProjection(DateOnly Date, decimal? Fund, decimal? AnnualIncome);

/// <summary>One pension on the dashboard.</summary>
/// <param name="Value">From its holdings when it has them, else its latest statement; null with neither.</param>
/// <param name="ValueDate">The date of the value: with holdings, the oldest of their prices.</param>
/// <param name="Cost">The holdings' book cost; null when the value isn't from holdings.</param>
/// <param name="History">Its statements' values oldest first, then the holdings' value if that's more recent.</param>
public sealed record PensionSummary(int Id, string Name, string Provider, decimal? Value, DateOnly? ValueDate, decimal? Cost,
    PensionProjection? Projection, IReadOnlyList<PensionValuePoint> History)
{
    public bool FromHoldings => Cost is not null;

    /// <summary>Value less cost; null when the value isn't from holdings.</summary>
    public decimal? Gain => Value - Cost;

    public decimal? GainPercent => Holdings.GainPercent(Gain, Cost ?? 0);
}

/// <summary>A person's pensions, largest value first, and their totals. Each counts once: from its holdings or its statement.</summary>
public sealed record PersonPensions(IReadOnlyList<PensionSummary> Pensions)
{
    private IEnumerable<PensionSummary> Valued => Pensions.Where(p => p.Value is not null);

    private IEnumerable<PensionSummary> WithHoldings => Pensions.Where(p => p.FromHoldings);

    private IEnumerable<PensionProjection> Projections => Pensions.Where(p => p.Projection is not null).Select(p => p.Projection!);

    public int ValuedCount => Valued.Count();

    public decimal TotalValue => Valued.Sum(p => p.Value!.Value);

    /// <summary>The oldest of the values' dates.</summary>
    public DateOnly? OldestValueDate => Valued.Min(p => p.ValueDate);

    public int WithHoldingsCount => WithHoldings.Count();

    public decimal TotalCost => WithHoldings.Sum(p => p.Cost!.Value);

    /// <summary>The gain on the pensions valued from holdings.</summary>
    public decimal TotalGain => WithHoldings.Sum(p => p.Gain!.Value);

    public decimal? TotalGainPercent => Holdings.GainPercent(TotalGain, TotalCost);

    public decimal? ProjectedFund => Projections.Sum(p => p.Fund);

    public decimal? ProjectedAnnualIncome => Projections.Sum(p => p.AnnualIncome);

    public bool HasProjections => Projections.Any();

    /// <summary>The oldest of the projections' statement dates.</summary>
    public DateOnly? OldestProjectionDate => Projections.Select(p => (DateOnly?)p.Date).Min();
}

/// <summary>
/// Sums up a pension for the dashboard: its value from <see cref="Holdings"/> when every fund held has a price, as
/// they're the most up to date, else its latest statement. Pure, so unit tested; <see cref="PensionHistory"/> loads it.
/// </summary>
public static class PensionOverview
{
    public static PensionSummary Of(int id, string name, string provider, IReadOnlyList<FundHolding> holdings, IReadOnlyList<StatementSummary> statements)
    {
        // One point a date: two kinds of statement can share one.
        var history = statements.GroupBy(s => s.Date).OrderBy(g => g.Key).Select(g => new PensionValuePoint(g.Key, g.Last().Value)).ToList();
        var projected = statements.Where(s => s.ProjectedFund is not null || s.ProjectedAnnualIncome is not null).MaxBy(s => s.Date);
        var projection = projected is null ? null : new PensionProjection(projected.Date, projected.ProjectedFund, projected.ProjectedAnnualIncome);

        var held = holdings.Where(h => !h.SoldOut).ToList();
        if (held.Count > 0 && held.All(h => h.Value is not null))
        {
            var value = held.Sum(h => h.Value!.Value);
            var date = held.Min(h => h.PriceDate!.Value);
            if (history.Count == 0 || date > history[^1].Date) history.Add(new PensionValuePoint(date, value));
            return new PensionSummary(id, name, provider, value, date, held.Sum(h => h.Cost), projection, history);
        }
        var latest = history.LastOrDefault();
        return new PensionSummary(id, name, provider, latest?.Value, latest?.Date, null, projection, history);
    }
}

/// <summary>Loads a person's pensions for the dashboard. A person has a few: their deals are loaded one pension at a time.</summary>
public sealed class PensionHistory(AppDbContext db)
{
    public async Task<PersonPensions> LoadAsync(int personId)
    {
        var pensions = await db.Pensions.Where(p => p.PersonId == personId)
            .Select(p => new { p.Id, p.Name, Provider = p.PensionProvider!.Name }).ToListAsync();
        if (pensions.Count == 0) return new PersonPensions([]);

        var ids = pensions.Select(p => p.Id).ToList();
        var statements = await db.PensionStatements.Where(s => ids.Contains(s.StatementType!.PensionId))
            .Select(s => new { s.StatementType!.PensionId, s.Date, s.Id, s.Value, s.ProjectedFund, s.ProjectedAnnualIncome })
            .ToListAsync();
        var funds = await db.PensionFunds.Where(f => ids.Contains(f.PensionId)).Select(f => new { f.PensionId, f.Fund, f.Isin }).ToListAsync();
        var prices = await db.LatestPricesAsync(funds.Select(f => f.Isin).Distinct().ToList());

        var summaries = new List<PensionSummary>();
        foreach (var pension in pensions)
        {
            var deals = await db.PensionTransactions.Where(tx => tx.PensionId == pension.Id).Deals().ToFundDealsAsync();
            var daily = funds.Where(f => f.PensionId == pension.Id && prices.ContainsKey(f.Isin))
                .ToDictionary(f => f.Fund, f => prices[f.Isin], StringComparer.OrdinalIgnoreCase);
            var own = statements.Where(s => s.PensionId == pension.Id).OrderBy(s => s.Date).ThenBy(s => s.Id)
                .Select(s => new StatementSummary(s.Date, s.Value, s.ProjectedFund, s.ProjectedAnnualIncome)).ToList();
            summaries.Add(PensionOverview.Of(pension.Id, pension.Name, pension.Provider, Holdings.Of(deals, daily), own));
        }
        return new PersonPensions(summaries.OrderByDescending(p => p.Value ?? 0).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
