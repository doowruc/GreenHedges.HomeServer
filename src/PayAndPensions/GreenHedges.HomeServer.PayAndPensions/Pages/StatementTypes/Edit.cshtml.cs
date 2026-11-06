using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.StatementTypes;

public class EditModel(AppDbContext db) : PensionPageModel(db)
{
    [BindProperty]
    public StatementTypeInput Type { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var type = await Db.StatementTypes.Where(t => t.Id == id && t.PensionId == PensionId)
            .Select(StatementTypeInput.Projection).FirstOrDefaultAsync();
        if (type is null) return NotFound();
        Type = type;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var existing = await Db.StatementTypes.FirstOrDefaultAsync(t => t.Id == id && t.PensionId == PensionId);
        if (existing is null) return NotFound();

        await StatementTypeRules.ValidateAsync(Db, PensionId, id, Type, ModelState);
        if (!ModelState.IsValid) return Page();

        Type.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Type.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this statement type after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Type.RowVersion = existing.RowVersion;
            ModelState.Remove("Type.RowVersion");
            return Page();
        }

        TempData["Message"] = "Statement type updated.";
        return RedirectToStatements();
    }
}
