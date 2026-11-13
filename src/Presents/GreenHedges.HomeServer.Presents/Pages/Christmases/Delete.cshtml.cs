using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;

namespace GreenHedges.HomeServer.Presents.Pages.Christmases;

/// <summary>Deletes a year's Christmas with its presents, after saying how many.</summary>
public class DeleteModel(AppDbContext db) : PageModel
{
    public int Year { get; private set; }

    public int Purchases { get; private set; }

    public async Task<IActionResult> OnGetAsync(int year)
    {
        var christmas = await db.Christmases.Where(c => c.Year == year).Select(c => new { Purchases = c.Purchases.Count }).FirstOrDefaultAsync();
        if (christmas is null) return NotFound();
        (Year, Purchases) = (year, christmas.Purchases);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int year)
    {
        // The database deletes its presents with it (FK cascade).
        var deleted = await db.Christmases.Where(c => c.Year == year).ExecuteDeleteAsync();
        if (deleted > 0) TempData["Message"] = $"Deleted Christmas {year}.";
        return RedirectToPage("/Index", new { year });
    }
}
