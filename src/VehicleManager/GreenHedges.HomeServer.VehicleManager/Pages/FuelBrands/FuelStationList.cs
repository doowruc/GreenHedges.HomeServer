using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands;

public class FuelStationList(AppDbContext db, CurrentUser user) : FuelPlaceList<FuelStation>(db, user)
{
    public override int MaxNameLength => FuelStation.MaxNameLength;

    /// <summary>One brand's stations, or every station (e.g. to merge into) if <paramref name="brandId"/> is null.</summary>
    public Task<List<FuelPlaceRow>> ListAsync(int? brandId = null) =>
        Rows(Db.FuelStations.Where(s => brandId == null || s.FuelBrandId == brandId)
            .OrderBy(s => s.Name).ThenBy(s => s.FuelBrand!.Name)).ToListAsync();

    public override Task<FuelPlaceRow?> FindAsync(int id) =>
        Rows(Db.FuelStations.Where(s => s.Id == id)).FirstOrDefaultAsync();

    private static IQueryable<FuelPlaceRow> Rows(IQueryable<FuelStation> stations) =>
        stations.Select(s => new FuelPlaceRow(s.Id, s.Name, s.FuelBrandId, s.FuelBrand!.Name, s.Stops.Count, null));

    public override Task<FuelPlaceInput?> GetForEditAsync(int id) =>
        Db.FuelStations.Where(s => s.Id == id)
            .Select(s => new FuelPlaceInput { Name = s.Name, BrandId = s.FuelBrandId, RowVersion = s.RowVersion })
            .FirstOrDefaultAsync();

    protected override async Task CheckAsync(int id, FuelPlaceInput input, ModelStateDictionary state)
    {
        if (input.BrandId is not { } brandId || !await Db.FuelBrands.AnyAsync(b => b.Id == brandId))
            state.AddModelError("Place.BrandId", "Choose the brand.");
        else if (await Db.FuelStations.AnyAsync(s => s.Id != id && s.FuelBrandId == brandId && s.Name == input.Name))
            state.AddModelError("Place.Name", "That brand already has a station with this name. To combine them, use Merge instead.");
    }

    protected override void Apply(FuelPlaceInput input, FuelStation station)
    {
        station.Name = input.Name;
        station.FuelBrandId = input.BrandId!.Value;
    }

    protected override Task<int> MergeCoreAsync(int fromId, int intoId, string userId, DateTime now) =>
        MoveStopsAsync(fromId, intoId, userId, now);

    protected override Task<bool> InUseAsync(int id) => Db.FuelEntryStops.AnyAsync(s => s.FuelStationId == id);
}
