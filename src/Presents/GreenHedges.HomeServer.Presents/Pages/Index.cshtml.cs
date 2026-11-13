using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.Models;
using GreenHedges.HomeServer.Presents.Pages.Purchases;
using GreenHedges.HomeServer.Presents.Services;
using GreenHedges.HomeServer.Presents.ViewModels;

namespace GreenHedges.HomeServer.Presents.Pages;

/// <summary>
/// A Christmas: this year's at /, another's at /{year}. A card per child, the form to add a present, and the year's
/// presents, latest first. The year's Christmas is made by its first present (or by setting its budget), so any year
/// can be shown.
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider time) : PageModel
{
    public const int FirstYear = 2000, LastYear = 2999;

    public record ChildRow(int Id, string Name, int Presents);

    public record PurchaseRow(int Id, DateOnly? Date, string Child, string Item, string? Shop, int Quantity, decimal Price, decimal Share, string? Splits);

    [BindProperty]
    public PurchaseInput Purchase { get; set; } = new();

    public int Year { get; private set; }

    public int ThisYear { get; private set; }

    /// <summary>Whether the year has a Christmas yet (it has presents, or a budget was set).</summary>
    public bool Exists { get; private set; }

    public decimal? Budget { get; private set; }

    /// <summary>The years with a Christmas, with this year and the one shown, latest first: for the picker.</summary>
    public List<int> Years { get; private set; } = [];

    public List<ChildRow> Children { get; private set; } = [];

    public List<PurchaseRow> Purchases { get; private set; } = [];

    /// <summary>The children for the form's "For".</summary>
    public IReadOnlyList<(int Id, string Name)> ChildChoices => Children.Select(c => (c.Id, c.Name)).ToList();

    /// <summary>Every shop used before, for the shop field's suggestions.</summary>
    public IReadOnlyList<string> Shops { get; private set; } = [];

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <param name="child">Who the form starts on: after adding a present, the same child.</param>
    /// <param name="shop">As <paramref name="child"/>: the same shop.</param>
    /// <param name="date">As <paramref name="child"/>: the same date (none is today's this year, and empty in other years).</param>
    public async Task<IActionResult> OnGetAsync(int? year, int? child, string? shop, DateOnly? date)
    {
        if (!SetYear(year)) return NotFound();
        Purchase.ChildId = child;
        Purchase.Shop = shop;
        Purchase.Date = date ?? (Year == ThisYear ? Today : null);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? year)
    {
        if (!SetYear(year)) return NotFound();
        ModelState.Remove("Purchase.Year");   // the page's year, not the form's
        await PurchaseRules.CheckAsync(db, Purchase, Today, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var purchase = new Purchase { Christmas = await PurchaseRules.ChristmasForAsync(db, Year) };
        Purchase.ApplyTo(purchase);
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync();

        var name = await db.Children.Where(c => c.Id == purchase.ChildId).Select(c => c.Name).FirstAsync();
        TempData["Message"] = $"Added {purchase.Item} for {name}.";
        // Back to the same child, shop and date, as presents often come several at a time.
        return RedirectToPage("/Index", new { year = Year, child = purchase.ChildId, shop = purchase.Shop, date = purchase.Date?.ToString("yyyy-MM-dd") });
    }

    private bool SetYear(int? year)
    {
        ThisYear = time.GetLocalNow().Year;
        Year = year ?? ThisYear;
        return Year is >= FirstYear and <= LastYear;
    }

    private async Task LoadAsync()
    {
        var year = Year;
        var christmas = await db.Christmases.Where(c => c.Year == year).Select(c => new { c.Budget }).FirstOrDefaultAsync();
        (Exists, Budget) = (christmas is not null, christmas?.Budget);

        Years = (await db.Christmases.Select(c => c.Year).ToListAsync())
            .Append(ThisYear).Append(Year).Distinct().OrderDescending().ToList();

        Children = await db.Children
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new ChildRow(c.Id, c.Name, c.Purchases.Where(p => p.Christmas!.Year == year).Sum(p => (int?)p.Quantity) ?? 0))
            .ToListAsync();

        var purchases = await db.Purchases.Where(p => p.Christmas!.Year == year)
            .Select(p => new { p.Id, p.Date, Child = p.Child!.Name, p.Item, p.Shop, p.Quantity, p.Price, p.SplitWithMum, p.SplitWithBirthday })
            .ToListAsync();
        // Latest first; undated ones after the dated, newest added first.
        Purchases = purchases
            .OrderBy(p => p.Date is null).ThenByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new PurchaseRow(p.Id, p.Date, p.Child, p.Item, p.Shop, p.Quantity, p.Price,
                Shares.Of(p.Price, p.Quantity, p.SplitWithMum, p.SplitWithBirthday), Shares.Describe(p.SplitWithMum, p.SplitWithBirthday)))
            .ToList();

        Shops = (await db.Purchases.Where(p => p.Shop != null).Select(p => p.Shop!).Distinct().ToListAsync())
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
