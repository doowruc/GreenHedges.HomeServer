using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Statements;

public class EditModel(AppDbContext db) : StatementFormModel(db)
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var statement = await Db.PensionStatements.Where(st => st.Id == id && st.StatementType!.PensionId == PensionId)
            .Select(st => new StatementInput
            {
                StatementTypeId = st.StatementTypeId, Date = st.Date, Label = st.Label, Value = st.Value,
                ProjectedFund = st.ProjectedFund, ProjectedAnnualIncome = st.ProjectedAnnualIncome, Notes = st.Notes,
                RowVersion = st.RowVersion
            })
            .FirstOrDefaultAsync();
        if (statement is null) return NotFound();
        Statement = statement;
        await LoadTypesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var existing = await Db.PensionStatements.FirstOrDefaultAsync(st => st.Id == id && st.StatementType!.PensionId == PensionId);
        if (existing is null) return NotFound();

        await LoadTypesAsync();
        await ValidateAsync(id);
        if (!ModelState.IsValid) return Page();

        Statement.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Statement.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this statement after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Statement.RowVersion = existing.RowVersion;
            ModelState.Remove("Statement.RowVersion");
            return Page();
        }

        TempData["Message"] = $"Updated the statement for {existing.Date:d}.";
        return RedirectToStatements();
    }
}
