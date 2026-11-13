using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.Models;
using GreenHedges.HomeServer.Presents.ViewModels;

namespace GreenHedges.HomeServer.Presents.Pages.Christmases;

/// <summary>A year's budget per child. Saving makes the year's Christmas if it hasn't one yet.</summary>
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public ChristmasInput Christmas { get; set; } = new();

    public int Year { get; private set; }

    /// <summary>Whether the year has a Christmas yet; only then can it be deleted.</summary>
    public bool Exists { get; private set; }

    public async Task<IActionResult> OnGetAsync(int year)
    {
        if (!SetYear(year)) return NotFound();
        var existing = await db.Christmases.Where(c => c.Year == year)
            .Select(c => new ChristmasInput { Budget = c.Budget, RowVersion = c.RowVersion }).FirstOrDefaultAsync();
        Exists = existing is not null;
        Christmas = existing ?? new();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int year)
    {
        if (!SetYear(year)) return NotFound();
        var existing = await db.Christmases.FirstOrDefaultAsync(c => c.Year == year);
        Exists = existing is not null;
        if (Christmas.Budget is { } budget && budget != Math.Round(budget, 2))
            ModelState.AddModelError("Christmas.Budget", "The budget is in pounds and pence: at most two decimal places.");
        if (!ModelState.IsValid) return Page();

        if (existing is null)
        {
            db.Christmases.Add(new Christmas { Year = year, Budget = Christmas.Budget });
            await db.SaveChangesAsync();
        }
        else
        {
            existing.Budget = Christmas.Budget;
            if (!await db.TrySaveEditAsync(existing, Christmas.RowVersion))
            {
                if (db.Entry(existing).State == EntityState.Detached) return NotFound();
                ModelState.AddModelError(string.Empty,
                    "Someone changed this Christmas after you opened it, so your changes haven't been saved. " +
                    "Reload the page to see the latest version, or save again to overwrite it with yours.");
                Christmas.RowVersion = existing.RowVersion;
                ModelState.Remove("Christmas.RowVersion");
                return Page();
            }
        }

        TempData["Message"] = Christmas.Budget is null ? $"No budget for Christmas {year}." : $"Set the budget for Christmas {year}.";
        return RedirectToPage("/Index", new { year });
    }

    private bool SetYear(int year)
    {
        Year = year;
        return year is >= IndexModel.FirstYear and <= IndexModel.LastYear;
    }
}
