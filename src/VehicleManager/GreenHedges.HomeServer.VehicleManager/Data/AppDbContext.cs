using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Data;

/// <remarks>
/// Each signed-in user has their own data. Query filters limit every query to the current user's
/// owners, vehicles, fuel brands and stations, settings, and (through their vehicle) fill-ups and their stops,
/// service records, MOT tests and their defects, and sent reminders.
/// Saving stamps new user-owned rows with the current user's id and fills in every table's audit columns
/// (<see cref="AuditedDbContext"/>). With no signed-in user (e.g. at startup) queries return nothing and saving throws. Use
/// <c>IgnoreQueryFilters()</c> only for deliberate cross-user work.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? currentUser = null)
    : AuditedDbContext(options, currentUser)
{
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<FuelEntry> FuelEntries => Set<FuelEntry>();
    public DbSet<FuelEntryStop> FuelEntryStops => Set<FuelEntryStop>();
    public DbSet<FuelBrand> FuelBrands => Set<FuelBrand>();
    public DbSet<FuelStation> FuelStations => Set<FuelStation>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<SentReminder> SentReminders => Set<SentReminder>();
    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();
    public DbSet<MotTest> MotTests => Set<MotTest>();
    public DbSet<MotDefect> MotDefects => Set<MotDefect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Owner>(e =>
        {
            e.HasQueryFilter(o => o.UserId == CurrentUserId);
            e.HasIndex(o => new { o.UserId, o.Name }).IsUnique();
            e.HasMany(o => o.Vehicles)
             .WithOne(v => v.Owner)
             .HasForeignKey(v => v.OwnerId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vehicle>(e =>
        {
            e.HasQueryFilter(v => v.UserId == CurrentUserId);
            e.Property(v => v.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(v => v.FuelType).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(v => new { v.UserId, v.RegistrationNumber }).IsUnique();
            e.HasMany(v => v.FuelEntries)
             .WithOne(f => f.Vehicle)
             .HasForeignKey(f => f.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(v => v.ServiceRecords)
             .WithOne(s => s.Vehicle)
             .HasForeignKey(s => s.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(v => v.MotTests)
             .WithOne(t => t.Vehicle)
             .HasForeignKey(t => t.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany<SentReminder>()
             .WithOne(r => r.Vehicle)
             .HasForeignKey(r => r.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServiceRecord>(e =>
        {
            e.HasQueryFilter(s => s.Vehicle!.UserId == CurrentUserId);
            e.HasIndex(s => new { s.VehicleId, s.Date });
        });

        modelBuilder.Entity<MotTest>(e =>
        {
            e.HasQueryFilter(t => t.Vehicle!.UserId == CurrentUserId);
            e.HasIndex(t => new { t.VehicleId, t.CompletedDate });
            e.HasMany(t => t.Defects)
             .WithOne(d => d.MotTest)
             .HasForeignKey(d => d.MotTestId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MotDefect>(e =>
        {
            e.HasQueryFilter(d => d.MotTest!.Vehicle!.UserId == CurrentUserId);
            e.HasIndex(d => new { d.MotTestId, d.Sequence }).IsUnique();
        });

        modelBuilder.Entity<SentReminder>(e =>
        {
            e.HasQueryFilter(r => r.Vehicle!.UserId == CurrentUserId);
            e.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
            // Unfiltered, so date-based reminders (no mileage) are unique too: SQL Server's unique index counts NULLs as equal.
            e.HasIndex(r => new { r.VehicleId, r.Kind, r.DueDate, r.DueMileage, r.DaysBefore }).IsUnique().HasFilter(null);
        });

        modelBuilder.Entity<FuelEntry>(e =>
        {
            e.HasQueryFilter(f => f.Vehicle!.UserId == CurrentUserId);
            e.HasIndex(f => new { f.VehicleId, f.Date });
            e.Property(f => f.Litres).HasPrecision(8, 3);
            e.Property(f => f.CostPerLitre).HasPrecision(6, 3);
            e.Property(f => f.VoucherPerLitre).HasPrecision(6, 3);
            e.Property(f => f.TripAverageMpg).HasPrecision(5, 1);
            e.Property(f => f.TripMiles).HasPrecision(7, 1);
            e.HasMany(f => f.Stops)
             .WithOne(s => s.FuelEntry)
             .HasForeignKey(s => s.FuelEntryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FuelEntryStop>(e =>
        {
            e.HasQueryFilter(s => s.FuelEntry!.Vehicle!.UserId == CurrentUserId);
            e.HasIndex(s => new { s.FuelEntryId, s.Sequence }).IsUnique();
        });

        // Brands with stations, and stations in use, can't be deleted; merge them instead (Pages/Places).
        modelBuilder.Entity<FuelBrand>(e =>
        {
            e.HasQueryFilter(b => b.UserId == CurrentUserId);
            e.HasIndex(b => new { b.UserId, b.Name }).IsUnique();
            e.HasMany(b => b.Stations)
             .WithOne(s => s.FuelBrand)
             .HasForeignKey(s => s.FuelBrandId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FuelStation>(e =>
        {
            e.HasQueryFilter(s => s.UserId == CurrentUserId);
            e.HasIndex(s => new { s.FuelBrandId, s.Name }).IsUnique();
            e.HasMany(s => s.Stops)
             .WithOne(st => st.FuelStation)
             .HasForeignKey(st => st.FuelStationId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserSettings>(e =>
        {
            e.HasQueryFilter(s => s.UserId == CurrentUserId);
            e.HasIndex(s => s.UserId).IsUnique();
        });
    }
}
