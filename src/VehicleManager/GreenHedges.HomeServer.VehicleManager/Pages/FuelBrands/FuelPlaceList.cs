using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands;

/// <summary>Database work shared by the Fuel Brands pages and their Stations pages.</summary>
public abstract class FuelPlaceList<T>(AppDbContext db, CurrentUser user)
    where T : AuditedEntity, IFuelPlace
{
    public abstract int MaxNameLength { get; }

    protected AppDbContext Db => db;

    public abstract Task<FuelPlaceRow?> FindAsync(int id);
    public abstract Task<FuelPlaceInput?> GetForEditAsync(int id);
    /// <summary>Checks a tidied name and brand; add errors to <paramref name="state"/>.</summary>
    protected abstract Task CheckAsync(int id, FuelPlaceInput input, ModelStateDictionary state);
    protected abstract void Apply(FuelPlaceInput input, T place);
    protected abstract Task<int> MergeCoreAsync(int fromId, int intoId, string userId, DateTime now);
    protected abstract Task<bool> InUseAsync(int id);

    /// <summary>Tidies the input and adds any errors to <paramref name="state"/> (keys <c>Place.Name</c>, <c>Place.BrandId</c>).</summary>
    public async Task ValidateAsync(int id, FuelPlaceInput input, ModelStateDictionary state)
    {
        input.Name = FuelStops.NormaliseName(input.Name);
        if (input.Name.Length > MaxNameLength)
            state.AddModelError("Place.Name", $"The name can be at most {MaxNameLength} characters.");
        else if (input.Name.Length > 0)
            await CheckAsync(id, input, state);
    }

    /// <summary>
    /// Saves the edit, unless it changed since the form was loaded (a conflict). Returns its current
    /// RowVersion, to put back in the form after a conflict.
    /// </summary>
    public async Task<(SaveResult Result, byte[] RowVersion)> SaveAsync(int id, FuelPlaceInput input)
    {
        var place = await db.Set<T>().FirstOrDefaultAsync(p => p.Id == id);
        if (place is null) return (SaveResult.NotFound, []);

        Apply(input, place);
        if (await db.TrySaveEditAsync(place, input.RowVersion)) return (SaveResult.Saved, place.RowVersion);
        return db.Entry(place).State == EntityState.Detached
            ? (SaveResult.NotFound, [])
            : (SaveResult.Conflict, place.RowVersion);
    }

    /// <summary>Moves everything from one to the other, then deletes the first. Returns the fill-up stops moved.</summary>
    public async Task<int> MergeAsync(int fromId, int intoId)
    {
        var userId = user.Id ?? throw new InvalidOperationException("Can't merge without a signed-in user.");
        var now = DateTime.UtcNow;
        // Bulk updates bypass SaveChanges, so the merge sets the audit columns itself. The retrying
        // execution strategy needs the whole transaction inside it.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var moved = await MergeCoreAsync(fromId, intoId, userId, now);
            await db.Set<T>().Where(p => p.Id == fromId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            return moved;
        });
    }

    /// <summary>Deletes it if nothing uses it.</summary>
    public async Task<bool> DeleteIfUnusedAsync(int id) =>
        !await InUseAsync(id) && await db.Set<T>().Where(p => p.Id == id).ExecuteDeleteAsync() > 0;

    /// <summary>Points the stops at one station to another.</summary>
    protected Task<int> MoveStopsAsync(int fromStationId, int intoStationId, string userId, DateTime now) =>
        db.FuelEntryStops.Where(s => s.FuelStationId == fromStationId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.FuelStationId, intoStationId)
            .SetProperty(x => x.ModifiedBy, userId)
            .SetProperty(x => x.ModifiedDate, now));
}
