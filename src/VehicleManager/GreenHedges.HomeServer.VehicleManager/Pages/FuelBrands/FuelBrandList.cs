using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands;

public class FuelBrandList(AppDbContext db, CurrentUser user) : FuelPlaceList<FuelBrand>(db, user)
{
    public override int MaxNameLength => FuelBrand.MaxNameLength;

    public Task<List<FuelPlaceRow>> ListAsync() =>
        Rows(Db.FuelBrands.OrderBy(b => b.Name)).ToListAsync();

    public override Task<FuelPlaceRow?> FindAsync(int id) =>
        Rows(Db.FuelBrands.Where(b => b.Id == id)).FirstOrDefaultAsync();

    private static IQueryable<FuelPlaceRow> Rows(IQueryable<FuelBrand> brands) =>
        brands.Select(b => new FuelPlaceRow(b.Id, b.Name, null, null, b.Stations.SelectMany(s => s.Stops).Count(), b.Stations.Count));

    public override Task<FuelPlaceInput?> GetForEditAsync(int id) =>
        Db.FuelBrands.Where(b => b.Id == id)
            .Select(b => new FuelPlaceInput { Name = b.Name, RowVersion = b.RowVersion })
            .FirstOrDefaultAsync();

    protected override async Task CheckAsync(int id, FuelPlaceInput input, ModelStateDictionary state)
    {
        if (await Db.FuelBrands.AnyAsync(b => b.Id != id && b.Name == input.Name))
            state.AddModelError("Place.Name", "There's already a fuel brand with this name. To combine them, use Merge instead.");
    }

    protected override void Apply(FuelPlaceInput input, FuelBrand brand) => brand.Name = input.Name;

    /// <summary>
    /// Moves the brand's stations to the other brand. A station whose name the other brand already
    /// has is combined with that one (its fill-ups move across); the rest just change brand.
    /// </summary>
    protected override async Task<int> MergeCoreAsync(int fromId, int intoId, string userId, DateTime now)
    {
        var stations = await Db.FuelStations.Where(s => s.FuelBrandId == fromId)
            .Select(s => new { s.Id, s.Name, Stops = s.Stops.Count }).ToListAsync();
        var intoStations = await Db.FuelStations.Where(s => s.FuelBrandId == intoId)
            .ToDictionaryAsync(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        var moved = 0;
        foreach (var station in stations)
        {
            if (intoStations.TryGetValue(station.Name, out var twin))
            {
                moved += await MoveStopsAsync(station.Id, twin, userId, now);
                await Db.FuelStations.Where(s => s.Id == station.Id).ExecuteDeleteAsync();
            }
            else
            {
                moved += station.Stops;
                await Db.FuelStations.Where(s => s.Id == station.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.FuelBrandId, intoId)
                    .SetProperty(x => x.ModifiedBy, userId)
                    .SetProperty(x => x.ModifiedDate, now));
            }
        }
        return moved;
    }

    protected override Task<bool> InUseAsync(int id) => Db.FuelStations.AnyAsync(s => s.FuelBrandId == id);
}
