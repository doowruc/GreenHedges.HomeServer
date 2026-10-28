using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GreenHedges.HomeServer.PayAndPensions.Data;

/// <summary>
/// Lets <c>dotnet ef</c> create the context without running <c>Program.cs</c>, which needs the
/// Auth0 settings. Uses the connection string from user secrets if there is one, which only
/// matters for commands that touch the database (e.g. <c>database update</c>); adding a migration
/// doesn't connect.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = config.GetConnectionString("PayAndPensions")
            ?? "Server=localhost;Database=PayAndPensions;Integrated Security=true";
        return new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
    }
}
