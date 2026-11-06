using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Statements;

public class CreateModel(AppDbContext db) : StatementFormModel(db)
{
    /// <summary>A new statement is today's, of the type asked for (else the first), with that type's latest label to change.</summary>
    public async Task<IActionResult> OnGetAsync(int? typeId)
    {
        if (!await LoadPensionAsync()) return NotFound();
        await LoadTypesAsync();
        if (Types.Count == 0) return RedirectToPage("/StatementTypes/Create", new { personId = PersonId, pensionId = PensionId });

        var type = Types.FirstOrDefault(t => t.Id == typeId) ?? Types[0];
        Statement.StatementTypeId = type.Id;
        Statement.Date = DateOnly.FromDateTime(DateTime.Today);
        Statement.Label = await Db.PensionStatements.Where(st => st.StatementTypeId == type.Id)
            .OrderByDescending(st => st.Date).Select(st => st.Label).FirstOrDefaultAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();
        await LoadTypesAsync();
        await ValidateAsync(null);
        if (!ModelState.IsValid) return Page();

        var statement = new PensionStatement();
        Statement.ApplyTo(statement);
        Db.PensionStatements.Add(statement);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added the statement for {statement.Date:d}.";
        return RedirectToStatements();
    }
}
