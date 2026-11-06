using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Statements;

public class DeleteModel(AppDbContext db) : PensionPageModel(db)
{
    public string Description { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var statement = await Db.PensionStatements.Where(st => st.Id == id && st.StatementType!.PensionId == PensionId)
            .Select(st => new { Type = st.StatementType!.Name, st.Date, st.Label, st.Value }).FirstOrDefaultAsync();
        if (statement is null) return NotFound();
        Description = $"the {statement.Type} statement for {statement.Date:d}{(statement.Label is null ? "" : $" ({statement.Label})")}, £{statement.Value:N2}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        await Db.PensionStatements.Where(st => st.Id == id && st.StatementType!.PensionId == PensionId).ExecuteDeleteAsync();
        TempData["Message"] = "Statement deleted.";
        return RedirectToStatements();
    }
}
