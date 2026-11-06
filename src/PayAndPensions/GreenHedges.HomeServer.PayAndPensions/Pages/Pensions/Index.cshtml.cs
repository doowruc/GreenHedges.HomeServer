using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

public class IndexModel(AppDbContext db) : PersonPageModel(db)
{
    public record Row(int Id, string Name, string Provider, string? Website, PensionType Type, string? PolicyNumber, int Payslips, int Statements);

    public List<Row> Pensions { get; private set; } = [];

    /// <summary>Whether the user has any providers yet: a pension needs one.</summary>
    public bool HasProviders { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        Pensions = await Db.Pensions.Where(pn => pn.PersonId == PersonId)
            .OrderBy(pn => pn.Name)
            .Select(pn => new Row(pn.Id, pn.Name, pn.PensionProvider!.Name, pn.PensionProvider.Website, pn.Type, pn.PolicyNumber, pn.Payslips.Count, pn.StatementTypes.Sum(t => t.Statements.Count)))
            .ToListAsync();
        HasProviders = await Db.PensionProviders.AnyAsync();
        return Page();
    }
}
