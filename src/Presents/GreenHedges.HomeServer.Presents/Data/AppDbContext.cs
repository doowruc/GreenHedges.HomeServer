using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Presents.Models;

namespace GreenHedges.HomeServer.Presents.Data;

/// <remarks>
/// Each signed-in user has their own data. Query filters limit every query to the current user's children,
/// Christmases and (through their Christmas) purchases. Saving stamps new children and Christmases with the current
/// user's id and fills in every table's audit columns (<see cref="AuditedDbContext"/>). With no signed-in user (e.g. at
/// startup) queries return nothing and saving throws. Use <c>IgnoreQueryFilters()</c> only for deliberate cross-user
/// work.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? currentUser = null)
    : AuditedDbContext(options, currentUser)
{
    public DbSet<Child> Children => Set<Child>();
    public DbSet<Christmas> Christmases => Set<Christmas>();
    public DbSet<Purchase> Purchases => Set<Purchase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Child>(e =>
        {
            e.HasQueryFilter(c => c.UserId == CurrentUserId);
            e.HasIndex(c => new { c.UserId, c.Name }).IsUnique();
            // A child's purchases are never deleted along with them: a child with purchases can't be deleted.
            e.HasMany(c => c.Purchases)
             .WithOne(p => p.Child)
             .HasForeignKey(p => p.ChildId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Christmas>(e =>
        {
            e.HasQueryFilter(c => c.UserId == CurrentUserId);
            e.Property(c => c.Budget).HasPrecision(10, 2);
            e.HasIndex(c => new { c.UserId, c.Year }).IsUnique();
            e.HasMany(c => c.Purchases)
             .WithOne(p => p.Christmas)
             .HasForeignKey(p => p.ChristmasId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Purchase>(e =>
        {
            e.HasQueryFilter(p => p.Christmas!.UserId == CurrentUserId);
            e.Property(p => p.Price).HasPrecision(10, 2);
            e.HasIndex(p => new { p.ChristmasId, p.ChildId });
        });
    }
}
