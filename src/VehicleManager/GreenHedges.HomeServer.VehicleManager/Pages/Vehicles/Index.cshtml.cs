using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(VehicleView Vehicle, int FillUps);

    public List<Row> Vehicles { get; private set; } = [];

    /// <summary>Vehicles disposed of, most recently gone first, for the Previous vehicles section.</summary>
    public List<Row> Previous { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var vehicles = await db.Vehicles
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(VehicleView.Projection)
            .ToListAsync();
        var fillUps = await db.FuelEntries
            .GroupBy(f => f.VehicleId)
            .Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VehicleId, x => x.Count);

        var today = Reminders.UkToday();
        var rows = vehicles.Select(v => new Row(v, fillUps.GetValueOrDefault(v.Id))).ToList();
        Vehicles = rows.Where(r => !r.Vehicle.IsDisposed(today)).ToList();
        Previous = rows.Where(r => r.Vehicle.IsDisposed(today)).OrderByDescending(r => r.Vehicle.DisposedDate).ToList();
    }
}
