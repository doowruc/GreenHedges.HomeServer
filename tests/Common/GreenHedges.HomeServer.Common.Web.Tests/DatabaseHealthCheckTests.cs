using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.Common.Web.Tests;

public class DatabaseHealthCheckTests
{
    private sealed class AnyContext(DbContextOptions<AnyContext> options) : DbContext(options);

    [Fact]
    public async Task A_database_that_cant_be_reached_is_unhealthy()
    {
        await using var db = new AnyContext(new DbContextOptionsBuilder<AnyContext>()
            .UseSqlServer("Server=unreachable.invalid;Connect Timeout=2").Options);

        var result = await new DatabaseHealthCheck<AnyContext>(db).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
