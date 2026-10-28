using Microsoft.AspNetCore.Mvc;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Years;

/// <summary>Every tax year of a person's pay: its totals, the change on the year before, and a chart.</summary>
public class IndexModel(AppDbContext db, PayHistory history) : PersonPageModel(db)
{
    /// <summary>Newest first.</summary>
    public List<TaxYearTotals> Years { get; private set; } = [];

    public string ChartJson { get; private set; } = "{}";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        var pay = await history.LoadAsync(PersonId);
        Years = pay.TaxYears.Reverse().ToList();
        ChartJson = PayCharts.Yearly(pay.TaxYears);
        return Page();
    }
}
