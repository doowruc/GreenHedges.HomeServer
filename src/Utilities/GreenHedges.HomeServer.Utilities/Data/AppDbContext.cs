using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Data;

/// <remarks>
/// Each signed-in user has their own data. Query filters limit every query to the current user's meters and (through
/// their meter) readings. Saving stamps new meters with the current user's id and fills in every table's audit columns
/// (<see cref="AuditedDbContext"/>). With no signed-in user (e.g. at startup) queries return nothing and saving throws.
/// Use <c>IgnoreQueryFilters()</c> only for deliberate cross-user work.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? currentUser = null)
    : AuditedDbContext(options, currentUser)
{
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Reading> Readings => Set<Reading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meter>(e =>
        {
            e.HasQueryFilter(m => m.UserId == CurrentUserId);
            e.Property(m => m.Utility).HasConversion<string>().HasMaxLength(30);
            e.Property(m => m.Unit).HasConversion<string>().HasMaxLength(30);
            e.Property(m => m.StartReading).HasPrecision(12, 3);
            e.HasIndex(m => new { m.UserId, m.Utility, m.InstalledOn });
            // Readings are never deleted along with something else: a meter with readings can't be deleted.
            e.HasMany(m => m.Readings)
             .WithOne(r => r.Meter)
             .HasForeignKey(r => r.MeterId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reading>(e =>
        {
            e.HasQueryFilter(r => r.Meter!.UserId == CurrentUserId);
            e.Property(r => r.Value).HasPrecision(12, 3);
            e.HasIndex(r => new { r.MeterId, r.Date }).IsUnique();
        });
    }
}
