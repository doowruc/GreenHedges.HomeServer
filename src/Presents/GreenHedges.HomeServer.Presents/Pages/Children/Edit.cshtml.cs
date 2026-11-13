using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.ViewModels;

namespace GreenHedges.HomeServer.Presents.Pages.Children;

/// <summary>Renames a child.</summary>
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public ChildInput Child { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var child = await db.Children.Where(c => c.Id == id).Select(ChildInput.Projection).FirstOrDefaultAsync();
        if (child is null) return NotFound();
        Child = child;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Children.FirstOrDefaultAsync(c => c.Id == id);
        if (existing is null) return NotFound();

        await ChildRules.ValidateAsync(db, id, Child, ModelState);
        if (!ModelState.IsValid) return Page();

        Child.ApplyTo(existing);
        if (!await db.TrySaveEditAsync(existing, Child.RowVersion))
        {
            if (db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this child after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Child.RowVersion = existing.RowVersion;
            ModelState.Remove("Child.RowVersion");
            return Page();
        }

        TempData["Message"] = $"Updated {existing.Name}.";
        return RedirectToPage("Index");
    }
}
