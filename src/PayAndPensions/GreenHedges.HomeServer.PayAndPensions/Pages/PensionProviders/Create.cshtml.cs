using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PensionProviders;

public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public PensionProviderInput Provider { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await PensionProviderRules.ValidateAsync(db, null, Provider, ModelState);
        if (!ModelState.IsValid) return Page();

        var provider = new PensionProvider();
        Provider.ApplyTo(provider);
        db.PensionProviders.Add(provider);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {provider.Name}.";
        return RedirectToPage("Index");
    }
}
