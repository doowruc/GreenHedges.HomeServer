using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.StatementTypes;

/// <summary>Deletes a statement type, but not while it has statements.</summary>
public class DeleteModel(AppDbContext db) : PensionPageModel(db)
{
    public string TypeName { get; private set; } = string.Empty;

    public int Statements { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var type = await Db.StatementTypes.Where(t => t.Id == id && t.PensionId == PensionId)
            .Select(t => new { t.Name, Statements = t.Statements.Count }).FirstOrDefaultAsync();
        if (type is null) return NotFound();
        (TypeName, Statements) = (type.Name, type.Statements);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var type = Db.StatementTypes.Where(t => t.Id == id && t.PensionId == PensionId);
        var name = await type.Select(t => t.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToStatements();
        if (await Db.PensionStatements.AnyAsync(st => st.StatementTypeId == id))
            return RedirectToPage("Delete", new { personId = PersonId, pensionId = PensionId, id });

        await type.ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToStatements();
    }
}
