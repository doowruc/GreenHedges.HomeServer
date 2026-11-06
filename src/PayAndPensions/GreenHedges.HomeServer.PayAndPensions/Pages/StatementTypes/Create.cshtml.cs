using Microsoft.AspNetCore.Mvc;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.StatementTypes;

public class CreateModel(AppDbContext db) : PensionPageModel(db)
{
    [BindProperty]
    public StatementTypeInput Type { get; set; } = new();

    public async Task<IActionResult> OnGetAsync() => await LoadPensionAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();
        await StatementTypeRules.ValidateAsync(Db, PensionId, null, Type, ModelState);
        if (!ModelState.IsValid) return Page();

        var type = new StatementType { PensionId = PensionId };
        Type.ApplyTo(type);
        Db.StatementTypes.Add(type);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added {type.Name}.";
        return RedirectToStatements();
    }
}
