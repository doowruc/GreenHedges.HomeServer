using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;

namespace GreenHedges.HomeServer.Presents.Pages.Children;

/// <summary>Deletes a child. Not while they have purchases: those are never deleted along with something else.</summary>
public class DeleteModel(AppDbContext db) : PageModel
{
    public string ChildName { get; private set; } = string.Empty;

    public int Purchases { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var child = await db.Children.Where(c => c.Id == id)
            .Select(c => new { c.Name, Purchases = c.Purchases.Count })
            .FirstOrDefaultAsync();
        if (child is null) return NotFound();
        (ChildName, Purchases) = (child.Name, child.Purchases);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var name = await db.Children.Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToPage("Index");
        if (await db.Purchases.AnyAsync(p => p.ChildId == id)) return RedirectToPage("Delete", new { id });

        await db.Children.Where(c => c.Id == id).ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToPage("Index");
    }
}
