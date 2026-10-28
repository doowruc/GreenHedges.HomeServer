using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GreenHedges.HomeServer.Common.Web.Data;

/// <summary>
/// The <c>/health</c> check: healthy when the database answers. The registration's timeout bounds it, as the
/// retrying execution strategy would otherwise keep trying for a while when the database is down.
/// </summary>
public sealed class DatabaseHealthCheck<TContext>(TContext db) : IHealthCheck where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Can't connect to the database.");
}
