using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// Hands data from before per-user data existed (owners, vehicles, brands and stations with no
/// user id) to one person: the user whose verified email matches the <c>ExistingDataOwner</c>
/// setting, the first time they sign in. Their fill-ups follow their vehicles. Once claimed the
/// setting does nothing, so it can be removed.
/// </summary>
public class ExistingDataClaim(AppDbContext db, AllowedUsers allowedUsers, IConfiguration config, ILogger<ExistingDataClaim> logger)
{
    public async Task ClaimIfOwnerAsync(ClaimsPrincipal user)
    {
        if (config["ExistingDataOwner"] is not { Length: > 0 } ownerEmail) return;
        if (!allowedUsers.IsAllowed(user) ||
            !string.Equals(AllowedUsers.Email(user), ownerEmail.Trim(), StringComparison.OrdinalIgnoreCase) ||
            CurrentUser.IdOf(user) is not { } userId)
            return;

        // Unclaimed rows belong to no user, so the usual per-user query filters must be bypassed.
        var owners = db.Owners.IgnoreQueryFilters().Where(o => o.UserId == "");
        var vehicles = db.Vehicles.IgnoreQueryFilters().Where(v => v.UserId == "");
        var brands = db.FuelBrands.IgnoreQueryFilters().Where(b => b.UserId == "");
        var stations = db.FuelStations.IgnoreQueryFilters().Where(s => s.UserId == "");
        if (!await owners.AnyAsync() && !await vehicles.AnyAsync() && !await brands.AnyAsync() && !await stations.AnyAsync())
            return;

        // Bulk updates bypass SaveChanges, so set the audit columns here. The retrying execution
        // strategy needs the whole transaction inside it, so it can be replayed as one unit.
        var now = DateTime.UtcNow;
        var (ownerCount, vehicleCount) = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var ownerCount = await owners.ExecuteUpdateAsync(s => s
                .SetProperty(o => o.UserId, userId)
                .SetProperty(o => o.ModifiedBy, userId)
                .SetProperty(o => o.ModifiedDate, now));
            var vehicleCount = await vehicles.ExecuteUpdateAsync(s => s
                .SetProperty(v => v.UserId, userId)
                .SetProperty(v => v.ModifiedBy, userId)
                .SetProperty(v => v.ModifiedDate, now));
            await brands.ExecuteUpdateAsync(s => s
                .SetProperty(b => b.UserId, userId)
                .SetProperty(b => b.ModifiedBy, userId)
                .SetProperty(b => b.ModifiedDate, now));
            await stations.ExecuteUpdateAsync(s => s
                .SetProperty(st => st.UserId, userId)
                .SetProperty(st => st.ModifiedBy, userId)
                .SetProperty(st => st.ModifiedDate, now));
            await transaction.CommitAsync();
            return (ownerCount, vehicleCount);
        });

        // The user id rather than the email: logs may leave the app (Telemetry).
        logger.LogInformation("Gave {Owners} existing owner(s) and {Vehicles} vehicle(s) to {UserId}.",
            ownerCount, vehicleCount, userId);
    }
}
