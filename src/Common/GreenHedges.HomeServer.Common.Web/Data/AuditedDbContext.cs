using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;

namespace GreenHedges.HomeServer.Common.Web.Data;

/// <summary>
/// A web app's database, where each signed-in user has their own data. Saving stamps new <see cref="IUserOwned"/> rows
/// with the current user's id and fills in every <see cref="AuditedEntity"/>'s audit columns, and throws if nobody is
/// signed in. The app's query filters compare rows with <see cref="CurrentUserId"/>, so with no signed-in user (e.g. at
/// startup) queries return nothing.
/// </summary>
public abstract class AuditedDbContext(DbContextOptions options, CurrentUser? currentUser) : DbContext(options)
{
    /// <summary>For the query filters; EF Core evaluates it per query, as a parameter.</summary>
    protected string? CurrentUserId => currentUser?.Id;

    /// <summary>
    /// When set, new rows keep the audit values they already have (a restored backup's), and only
    /// missing ones are filled in. New user-owned rows still always belong to the current user.
    /// </summary>
    public bool KeepAuditValues { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Fills in the audit columns, and the owning user on new rows.</summary>
    private void StampChanges()
    {
        var entries = ChangeTracker.Entries<AuditedEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();
        if (entries.Count == 0) return;

        var userId = CurrentUserId
            ?? throw new InvalidOperationException("Can't save data without a signed-in user.");
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            var audit = entry.Entity;
            var keep = KeepAuditValues && entry.State == EntityState.Added;
            if (entry.State == EntityState.Added)
            {
                if (!keep || audit.AddedDate == default) audit.AddedDate = now;
                if (!keep || audit.AddedBy.Length == 0) audit.AddedBy = userId;
                if (audit is IUserOwned owned) owned.UserId = userId;
            }
            else
            {
                // Who added a row, and when, never changes.
                entry.Property(e => e.AddedDate).IsModified = false;
                entry.Property(e => e.AddedBy).IsModified = false;
            }
            if (!keep || audit.ModifiedDate == default) audit.ModifiedDate = now;
            if (!keep || audit.ModifiedBy.Length == 0) audit.ModifiedBy = userId;
        }
    }
}
