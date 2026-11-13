using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.Models;
using GreenHedges.HomeServer.Presents.ViewModels;

namespace GreenHedges.HomeServer.Presents.Pages.Children;

/// <summary>Adds a child, after the others.</summary>
public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public ChildInput Child { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await ChildRules.ValidateAsync(db, null, Child, ModelState);
        if (!ModelState.IsValid) return Page();

        var child = new Child { DisplayOrder = (await db.Children.MaxAsync(c => (int?)c.DisplayOrder) ?? 0) + 1 };
        Child.ApplyTo(child);
        db.Children.Add(child);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {child.Name}.";
        return RedirectToPage("Index");
    }
}
