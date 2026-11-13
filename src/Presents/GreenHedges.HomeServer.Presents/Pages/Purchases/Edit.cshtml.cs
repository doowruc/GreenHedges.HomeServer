using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.ViewModels;

namespace GreenHedges.HomeServer.Presents.Pages.Purchases;

/// <summary>Edits a present, including which Christmas it belongs to (that year's is made if there isn't one).</summary>
public class EditModel(AppDbContext db, TimeProvider time) : PageModel
{
    [BindProperty]
    public PurchaseInput Purchase { get; set; } = new();

    /// <summary>The year it was in when the page was opened: Cancel goes back there.</summary>
    public int FromYear { get; private set; }

    public IReadOnlyList<(int Id, string Name)> Children { get; private set; } = [];

    public IReadOnlyList<string> Shops { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var found = await db.Purchases.Where(p => p.Id == id).Select(p => new { Purchase = p, p.Christmas!.Year }).FirstOrDefaultAsync();
        if (found is null) return NotFound();
        Purchase = PurchaseInput.From(found.Purchase, found.Year);
        FromYear = found.Year;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var found = await db.Purchases.Where(p => p.Id == id).Select(p => new { Purchase = p, p.Christmas!.Year }).FirstOrDefaultAsync();
        if (found is null) return NotFound();
        var existing = found.Purchase;
        FromYear = found.Year;

        await PurchaseRules.CheckAsync(db, Purchase, DateOnly.FromDateTime(time.GetLocalNow().DateTime), ModelState);
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        Purchase.ApplyTo(existing);
        if (Purchase.Year != found.Year) existing.Christmas = await PurchaseRules.ChristmasForAsync(db, Purchase.Year);
        if (!await db.TrySaveEditAsync(existing, Purchase.RowVersion))
        {
            if (db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this present after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Purchase.RowVersion = existing.RowVersion;
            ModelState.Remove("Purchase.RowVersion");
            await LoadAsync();
            return Page();
        }

        TempData["Message"] = $"Updated {existing.Item}.";
        return RedirectToPage("/Index", new { year = Purchase.Year });
    }

    private async Task LoadAsync()
    {
        Children = (await db.Children.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync())
            .Select(c => (c.Id, c.Name)).ToList();
        Shops = (await db.Purchases.Where(p => p.Shop != null).Select(p => p.Shop!).Distinct().ToListAsync())
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
