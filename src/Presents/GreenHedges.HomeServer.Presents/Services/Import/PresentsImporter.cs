using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.Models;

namespace GreenHedges.HomeServer.Presents.Services.Import;

/// <summary>A child's cash for a year: the sheet's cash rows against what the app works out.</summary>
/// <param name="Sheet">The sheet's cash rows for them, added up; null if it has none.</param>
public sealed record CashCheck(string Name, decimal? Sheet, decimal App)
{
    public bool Matches => (Sheet ?? 0m) == App;
}

/// <summary>What importing a year's sheet would do.</summary>
/// <param name="AlreadyThere">The year is already here: it's left alone and nothing from its sheet is imported.</param>
/// <param name="ToImport">Its presents, each with its child's id (negative for a child to add).</param>
/// <param name="Budget">The budget the sheet's cash rows show was used (every child had cash and the totals are equal); null if none.</param>
/// <param name="Summary">The year's figures as they'd be here; null when it's already there.</param>
/// <param name="Cash">Each child's cash, the sheet's against the app's; empty when it's already there.</param>
/// <param name="CashRows">How many cash rows the sheet has (not imported: the app works cash out).</param>
public sealed record YearPlan(
    int Year,
    bool AlreadyThere,
    List<(SheetPurchase Purchase, int ChildId)> ToImport,
    decimal? Budget,
    ChristmasSummary? Summary,
    List<CashCheck> Cash,
    int CashRows,
    List<(int Row, string Reason)> Skipped);

/// <param name="NewChildren">Children to add, after those already here, with made-up negative ids.</param>
public sealed record ImportPlan(List<SummaryChild> NewChildren, List<YearPlan> Years, List<string> Warnings)
{
    public int Presents => Years.Sum(y => y.ToImport.Count);
    public IEnumerable<YearPlan> ToImport => Years.Where(y => y.ToImport.Count > 0);
}

/// <summary>Plans the import of the presents workbook, and imports it. The planning is pure (<see cref="Plan"/>).</summary>
public class PresentsImporter(AppDbContext db, TimeProvider time, ILogger<PresentsImporter> logger)
{
    public async Task<ImportPlan> PlanAsync(WorkbookContents contents)
    {
        var children = await db.Children.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).Select(c => new SummaryChild(c.Id, c.Name)).ToListAsync();
        var years = (await db.Christmases.Select(c => c.Year).ToListAsync()).ToHashSet();
        return Plan(contents, children, years, DateOnly.FromDateTime(time.GetLocalNow().DateTime));
    }

    /// <summary>
    /// Years already here are left alone. For the others: each present goes to the child the sheet names (matched
    /// ignoring case; a name not here yet becomes a new child, after the others, in the order the sheets first name
    /// them), at its amount, one of, with no splits. A date in the future is dropped. Where every child had a cash row
    /// and their totals (presents and cash) are equal, that total was the budget. The cash the app works out for each
    /// child is set beside the sheet's.
    /// </summary>
    public static ImportPlan Plan(WorkbookContents contents, IReadOnlyList<SummaryChild> existingChildren, IReadOnlySet<int> existingYears, DateOnly today)
    {
        var newYears = contents.Years.Where(y => !existingYears.Contains(y.Year)).ToList();
        var newChildren = new List<SummaryChild>();
        var ids = existingChildren.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var name in newYears.SelectMany(y => y.Purchases.Select(p => p.Who).Concat(y.Cash.Select(c => c.Who))))
        {
            if (ids.ContainsKey(name)) continue;
            var child = new SummaryChild(-(newChildren.Count + 1), name);
            newChildren.Add(child);
            ids[name] = child.Id;
        }
        List<SummaryChild> children = [.. existingChildren, .. newChildren];

        var years = contents.Years.Select(sheet =>
        {
            if (existingYears.Contains(sheet.Year))
                return new YearPlan(sheet.Year, true, [], null, null, [], sheet.Cash.Count, sheet.Skipped);

            var toImport = sheet.Purchases
                .Select(p => (Purchase: p.Date > today ? p with { Date = null } : p, ChildId: ids[p.Who]))
                .ToList();
            var spent = toImport.ToLookup(t => t.ChildId, t => t.Purchase.Amount);
            var sheetCash = sheet.Cash.GroupBy(c => ids[c.Who]).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
            var totals = children.Select(c => spent[c.Id].Sum() + sheetCash.GetValueOrDefault(c.Id)).Distinct().ToList();
            decimal? budget = children.Count > 0 && children.All(c => sheetCash.ContainsKey(c.Id)) && totals.Count == 1 ? totals[0] : null;

            var summary = Summary.For(sheet.Year, budget, children,
                toImport.Select(t => new SummaryPurchase(t.ChildId, t.Purchase.Amount, 1, false, false)));
            var cash = summary.Children
                .Select(c => new CashCheck(c.Name, sheetCash.TryGetValue(c.ChildId, out var s) ? s : null, c.Cash))
                .ToList();
            return new YearPlan(sheet.Year, false, toImport, budget, summary, cash, sheet.Cash.Count, sheet.Skipped);
        }).ToList();

        return new ImportPlan(newChildren, years, contents.Warnings);
    }

    /// <summary>Adds the plan's children, Christmases and presents, in one save.</summary>
    /// <returns>How many presents, years and children were added.</returns>
    public async Task<(int Presents, int Years, int Children)> ImportAsync(ImportPlan plan)
    {
        var order = await db.Children.MaxAsync(c => (int?)c.DisplayOrder) ?? 0;
        var added = plan.NewChildren.ToDictionary(c => c.Id, c => new Child { Name = c.Name, DisplayOrder = ++order });
        db.Children.AddRange(added.Values);

        var years = plan.Years.Where(y => !y.AlreadyThere && (y.ToImport.Count > 0 || y.Budget is not null)).ToList();
        foreach (var year in years)
        {
            var christmas = new Christmas { Year = year.Year, Budget = year.Budget };
            foreach (var (sheet, childId) in year.ToImport)
            {
                var purchase = new Purchase { Item = sheet.Item, Shop = sheet.Shop, Date = sheet.Date, Price = sheet.Amount, Quantity = 1 };
                if (added.TryGetValue(childId, out var child)) purchase.Child = child;
                else purchase.ChildId = childId;
                christmas.Purchases.Add(purchase);
            }
            db.Christmases.Add(christmas);
        }
        await db.SaveChangesAsync();

        var result = (Presents: years.Sum(y => y.ToImport.Count), Years: years.Count, Children: added.Count);
        // Counts only: never the names, presents or amounts.
        logger.LogInformation("Imported {Presents} presents in {Years} years and {Children} children from a spreadsheet",
            result.Presents, result.Years, result.Children);
        return result;
    }
}
