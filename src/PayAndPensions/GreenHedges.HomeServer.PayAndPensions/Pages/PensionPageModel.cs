using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages;

/// <summary>
/// Base for one pension's pages (its statements, statement types and transactions), routed under
/// <c>/People/{personId}/Pensions/{pensionId}/…</c>. Links and redirects between them must pass both ids.
/// </summary>
public abstract class PensionPageModel(AppDbContext db) : PersonPageModel(db)
{
    [BindProperty(SupportsGet = true)]
    public int PensionId { get; set; }

    public string PensionName { get; private set; } = string.Empty;

    public string ProviderName { get; private set; } = string.Empty;

    public DateOnly? StartDate { get; private set; }

    /// <summary>Loads the person and the pension from the URL; false unless the pension is one of the person's.</summary>
    protected async Task<bool> LoadPensionAsync()
    {
        if (!await LoadPersonAsync()) return false;
        var pension = await Db.Pensions.Where(pn => pn.Id == PensionId && pn.PersonId == PersonId)
            .Select(pn => new { pn.Name, Provider = pn.PensionProvider!.Name, pn.StartDate }).FirstOrDefaultAsync();
        if (pension is null) return false;
        (PensionName, ProviderName, StartDate) = (pension.Name, pension.Provider, pension.StartDate);
        return true;
    }

    protected RedirectToPageResult RedirectToStatements() =>
        RedirectToPage("/Statements/Index", new { personId = PersonId, pensionId = PensionId });

    /// <summary>To the transactions, in the tax year of <paramref name="date"/> (the latest when null).</summary>
    protected RedirectToPageResult RedirectToTransactions(DateOnly? date = null) =>
        RedirectToPage("/Transactions/Index", new { personId = PersonId, pensionId = PensionId, year = date is { } d ? TaxYears.Of(d) : (int?)null });
}
