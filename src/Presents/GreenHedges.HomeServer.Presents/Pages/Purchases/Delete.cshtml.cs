using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.Services;

namespace GreenHedges.HomeServer.Presents.Pages.Purchases;

/// <summary>Deletes a present, after asking.</summary>
public class DeleteModel(AppDbContext db) : PageModel
{
    public string Description { get; private set; } = string.Empty;

    public int Year { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var p = await db.Purchases.Where(p => p.Id == id)
            .Select(p => new { p.Item, Child = p.Child!.Name, p.Christmas!.Year, p.Price, p.Quantity, p.SplitWithMum, p.SplitWithBirthday })
            .FirstOrDefaultAsync();
        if (p is null) return NotFound();
        Year = p.Year;
        var share = Shares.Of(p.Price, p.Quantity, p.SplitWithMum, p.SplitWithBirthday);
        Description = $"{p.Item} for {p.Child} (Christmas {p.Year}, counting {share.ToString("C", CultureInfo.GetCultureInfo("en-GB"))})";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var year = await db.Purchases.Where(p => p.Id == id).Select(p => (int?)p.Christmas!.Year).FirstOrDefaultAsync();
        if (year is null) return RedirectToPage("/Index");
        await db.Purchases.Where(p => p.Id == id).ExecuteDeleteAsync();
        TempData["Message"] = "Present deleted.";
        return RedirectToPage("/Index", new { year });
    }
}
