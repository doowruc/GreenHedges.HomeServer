namespace GreenHedges.HomeServer.Presents.Services;

/// <summary>A child, as the summary needs them: in display order.</summary>
public sealed record SummaryChild(int Id, string Name);

/// <summary>A purchase, as the summary needs it.</summary>
public sealed record SummaryPurchase(int ChildId, decimal Price, int Quantity, bool SplitWithMum, bool SplitWithBirthday);

/// <param name="Presents">How many (a quantity of 2 is two).</param>
/// <param name="Spent">The sum of their purchases' shares.</param>
/// <param name="Cash">What tops them up to the target.</param>
/// <param name="Total">Spent + cash: the target, the same for every child.</param>
public sealed record ChildSummary(int ChildId, string Name, int Presents, decimal Spent, decimal Cash, decimal Total)
{
    /// <summary>Whether they spent more than the budget (so everyone else is topped up to them, not to it).</summary>
    public bool OverBudget { get; init; }
}

/// <param name="Target">What every child's Christmas comes to: the most any child spent, or the budget if more.</param>
public sealed record ChristmasSummary(int Year, decimal? Budget, decimal Target, IReadOnlyList<ChildSummary> Children)
{
    public decimal Spent => Children.Sum(c => c.Spent);
    public decimal Cash => Children.Sum(c => c.Cash);
    public decimal Total => Children.Sum(c => c.Total);
    public bool OverBudget => Children.Any(c => c.OverBudget);
}

/// <summary>A Christmas's figures, worked out from its purchases: never stored.</summary>
public static class Summary
{
    /// <summary>
    /// Each child's spend (their shares added up), the target (the most any child spent, or the budget if there is one
    /// and it's more), and the cash that brings each child up to it. Every child counts, with or without presents
    /// that year. Purchases for anyone not in <paramref name="children"/> are ignored.
    /// </summary>
    public static ChristmasSummary For(int year, decimal? budget, IReadOnlyList<SummaryChild> children, IEnumerable<SummaryPurchase> purchases)
    {
        var byChild = purchases.ToLookup(p => p.ChildId);
        var spent = children.Select(c => (Child: c,
            Spent: byChild[c.Id].Sum(p => Shares.Of(p.Price, p.Quantity, p.SplitWithMum, p.SplitWithBirthday)),
            Presents: byChild[c.Id].Sum(p => p.Quantity))).ToList();

        var target = Math.Max(budget ?? 0m, spent.Count == 0 ? 0m : spent.Max(s => s.Spent));
        var rows = spent.Select(s => new ChildSummary(s.Child.Id, s.Child.Name, s.Presents, s.Spent, target - s.Spent, target)
        {
            OverBudget = budget is { } b && s.Spent > b
        }).ToList();
        return new ChristmasSummary(year, budget, target, rows);
    }
}
