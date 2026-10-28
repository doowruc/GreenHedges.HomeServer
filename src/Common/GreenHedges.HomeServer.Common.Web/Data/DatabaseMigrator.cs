using Microsoft.EntityFrameworkCore;

namespace GreenHedges.HomeServer.Common.Web.Data;

public static class DatabaseMigrator
{
    /// <summary>Applies any pending EF Core migrations, creating the database's tables if needed.</summary>
    public static void MigrateDatabase(this DbContext db, ILogger logger)
    {
        var pending = db.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
            logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        db.Database.Migrate();
    }
}
