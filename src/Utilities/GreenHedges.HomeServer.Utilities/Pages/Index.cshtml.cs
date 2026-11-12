using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Pages;

/// <summary>The dashboard: each utility's latest reading.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    /// <param name="Readings">How many readings the utility has, over all its meters.</param>
    /// <param name="LatestDate">The latest reading's date; null with none.</param>
    public record UtilitySummary(UtilityType Utility, int Readings, DateOnly? LatestDate, decimal? LatestValue, MeterUnit? Unit);

    public List<UtilitySummary> Utilities { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // The enums are stored as text, so group in memory: a handful of meters.
        var meters = await db.Meters
            .Select(m => new
            {
                m.Utility,
                m.Unit,
                Count = m.Readings.Count,
                Latest = m.Readings.OrderByDescending(r => r.Date).Select(r => new { r.Date, r.Value }).FirstOrDefault()
            })
            .ToListAsync();

        Utilities = Enum.GetValues<UtilityType>()
            .Select(utility =>
            {
                var own = meters.Where(m => m.Utility == utility).ToList();
                var latest = own.Where(m => m.Latest is not null).OrderByDescending(m => m.Latest!.Date).FirstOrDefault();
                return new UtilitySummary(utility, own.Sum(m => m.Count), latest?.Latest!.Date, latest?.Latest!.Value, latest?.Unit);
            })
            .ToList();
    }
}
