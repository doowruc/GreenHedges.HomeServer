using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PensionProviders;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public PensionProviderInput Provider { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var provider = await db.PensionProviders.Where(pp => pp.Id == id).Select(PensionProviderInput.Projection).FirstOrDefaultAsync();
        if (provider is null) return NotFound();
        Provider = provider;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.PensionProviders.FirstOrDefaultAsync(pp => pp.Id == id);
        if (existing is null) return NotFound();

        await PensionProviderRules.ValidateAsync(db, id, Provider, ModelState);
        if (!ModelState.IsValid) return Page();

        Provider.ApplyTo(existing);
        if (!await db.TrySaveEditAsync(existing, Provider.RowVersion))
        {
            if (db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this provider after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Provider.RowVersion = existing.RowVersion;
            ModelState.Remove("Provider.RowVersion");
            return Page();
        }

        TempData["Message"] = "Provider updated.";
        return RedirectToPage("Index");
    }
}
