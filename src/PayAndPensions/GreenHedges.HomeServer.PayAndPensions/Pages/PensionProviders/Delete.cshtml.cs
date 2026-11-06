using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PensionProviders;

/// <summary>Deletes a pension provider, but not while anyone's pensions are with it.</summary>
public class DeleteModel(AppDbContext db) : PageModel
{
    public string ProviderName { get; private set; } = string.Empty;

    public int Pensions { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var provider = await db.PensionProviders.Where(pp => pp.Id == id)
            .Select(pp => new { pp.Name, Pensions = pp.Pensions.Count }).FirstOrDefaultAsync();
        if (provider is null) return NotFound();
        (ProviderName, Pensions) = (provider.Name, provider.Pensions);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var provider = db.PensionProviders.Where(pp => pp.Id == id);
        var name = await provider.Select(pp => pp.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToPage("Index");
        if (await db.Pensions.AnyAsync(pn => pn.PensionProviderId == id)) return RedirectToPage("Delete", new { id });

        await provider.ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToPage("Index");
    }
}
