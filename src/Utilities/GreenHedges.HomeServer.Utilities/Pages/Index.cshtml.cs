using Microsoft.AspNetCore.Mvc.RazorPages;
using GreenHedges.HomeServer.Utilities.Services;

namespace GreenHedges.HomeServer.Utilities.Pages;

/// <summary>The dashboard: each utility's latest reading and what was used a day up to it.</summary>
public class IndexModel(UtilityReadings readings) : PageModel
{
    public List<UtilityHistory> Utilities { get; private set; } = [];

    public async Task OnGetAsync() => Utilities = await readings.LoadAllAsync();
}
