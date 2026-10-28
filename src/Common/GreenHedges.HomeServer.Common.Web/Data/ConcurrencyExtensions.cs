using Microsoft.EntityFrameworkCore;
namespace GreenHedges.HomeServer.Common.Web.Data;

public static class ConcurrencyExtensions
{
    /// <summary>
    /// Saves an edit only if the row hasn't changed since the form was loaded, i.e. its
    /// <see cref="AuditedEntity.RowVersion"/> still matches <paramref name="loadedRowVersion"/>.
    /// </summary>
    /// <returns>
    /// False if someone else changed or deleted the row in the meantime. Nothing is saved, and
    /// <paramref name="entity"/> is reloaded with the latest values (or detached if it was deleted).
    /// </returns>
    public static async Task<bool> TrySaveEditAsync<T>(this DbContext db, T entity, byte[] loadedRowVersion)
        where T : AuditedEntity
    {
        db.Entry(entity).Property(e => e.RowVersion).OriginalValue = loadedRowVersion;
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(entity).ReloadAsync();
            return false;
        }
    }
}
