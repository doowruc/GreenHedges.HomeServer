using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages;

public class IndexModel(AppDbContext db, FuelStatsService stats) : PageModel
{
    public record VehicleCard(VehicleView Vehicle, FuelSummary AllTime, FuelSummary Year, FuelEntryView? Last, List<DueDateView> DueDates);

    public List<VehicleCard> Cards { get; private set; } = [];

    /// <summary>Due dates within <see cref="Reminders.SoonDays"/> days, or overdue, for every vehicle, soonest first.</summary>
    public List<DueDateView> ComingUp { get; private set; } = [];

    /// <summary>Fuel spend charts (wwwroot/js/spend-chart.js): the last 12 months by month, and all time by year.</summary>
    public string? MonthlySpendJson { get; private set; }
    public string? YearlySpendJson { get; private set; }

    /// <summary>Each year's spend, newest first: the year-on-year chart (<see cref="YearOnYearJson"/>) and table.</summary>
    public List<YearSpend> Years { get; private set; } = [];
    public string? YearOnYearJson { get; private set; }

    public async Task OnGetAsync()
    {
        var vehicles = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(VehicleView.Projection)
            .ToListAsync();
        var entries = (await db.FuelEntries
                .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                .Select(FuelEntryView.Projection)
                .ToListAsync())
            .ToLookup(f => f.VehicleId);

        var motReadings = await MileageTrend.MotReadingsAsync(db);

        var today = Reminders.UkToday();
        var dueDates = vehicles.ToDictionary(v => v.Id, v => v.DueDates(today, MileageTrend.From(entries[v.Id], motReadings[v.Id])));
        ComingUp = dueDates.Values.SelectMany(d => d).Where(d => d.IsSoon)
            .OrderBy(d => d.Date).ThenBy(d => d.VehicleName).ThenBy(d => d.Kind)
            .ToList();

        var yearAgo = DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
        // Vehicles disposed of keep their spend in the charts, but have no card.
        Cards = vehicles.Where(v => !v.IsDisposed(today)).Select(v => new VehicleCard(
                v,
                stats.Summarise(entries[v.Id]),
                stats.Summarise(entries[v.Id], yearAgo),
                entries[v.Id].FirstOrDefault(),
                dueDates[v.Id]))
            .OrderByDescending(c => c.Last?.Date)
            .ToList();

        Years = SpendStats.Years(entries.SelectMany(e => e), today);
        if (Years.Count > 0)
        {
            // Every vehicle keeps its colour (an index into the script's palette) and name in both charts.
            var duplicateNames = vehicles.GroupBy(v => v.DisplayName).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            var series = vehicles.Select((v, i) => (v.Id, Colour: i, Name: duplicateNames.Contains(v.DisplayName) ? $"{v.DisplayName} ({v.RegistrationNumber})" : v.DisplayName))
                .ToDictionary(v => v.Id);
            var ids = vehicles.Select(v => v.Id).ToList();
            string Json(SpendBreakdown spend) => JsonSerializer.Serialize(new
            {
                labels = spend.Labels,
                vehicles = spend.Vehicles.Select(v => new { name = series[v.VehicleId].Name, colour = series[v.VehicleId].Colour, amounts = v.Amounts })
            });
            MonthlySpendJson = Json(SpendStats.Monthly(entries.SelectMany(e => e), ids, today));
            YearlySpendJson = Json(SpendStats.Yearly(entries.SelectMany(e => e), ids, today));
            YearOnYearJson = JsonSerializer.Serialize(Years.Select(y => new { year = y.Year, cumulative = y.Cumulative }));
        }
    }
}
