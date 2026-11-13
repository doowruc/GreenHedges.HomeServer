using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;

namespace GreenHedges.HomeServer.Presents.Services;

/// <summary>Loads what <see cref="Summary"/> needs from the database, for a year or for every year.</summary>
public sealed class ChristmasSummaries(AppDbContext db)
{
    /// <summary>A year's summary, with or without a Christmas yet (none: no budget and no presents).</summary>
    public async Task<ChristmasSummary> ForYearAsync(int year)
    {
        var budget = await db.Christmases.Where(c => c.Year == year).Select(c => c.Budget).FirstOrDefaultAsync();
        var purchases = await db.Purchases.Where(p => p.Christmas!.Year == year)
            .Select(p => new SummaryPurchase(p.ChildId, p.Price, p.Quantity, p.SplitWithMum, p.SplitWithBirthday))
            .ToListAsync();
        return Summary.For(year, budget, await ChildrenAsync(), purchases);
    }

    /// <summary>Every year with a Christmas, latest first.</summary>
    public async Task<List<ChristmasSummary>> AllAsync()
    {
        var children = await ChildrenAsync();
        var christmases = await db.Christmases.Select(c => new { c.Year, c.Budget }).ToListAsync();
        var purchases = (await db.Purchases
                .Select(p => new { p.Christmas!.Year, Purchase = new SummaryPurchase(p.ChildId, p.Price, p.Quantity, p.SplitWithMum, p.SplitWithBirthday) })
                .ToListAsync())
            .ToLookup(p => p.Year, p => p.Purchase);
        return christmases.OrderByDescending(c => c.Year)
            .Select(c => Summary.For(c.Year, c.Budget, children, purchases[c.Year]))
            .ToList();
    }

    private async Task<List<SummaryChild>> ChildrenAsync() =>
        await db.Children.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).Select(c => new SummaryChild(c.Id, c.Name)).ToListAsync();
}
