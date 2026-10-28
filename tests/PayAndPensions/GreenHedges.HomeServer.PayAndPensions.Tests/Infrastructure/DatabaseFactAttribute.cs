namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>A test that needs the database; skipped when no SQL Server for the tests is configured.</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!TestDatabase.IsConfigured)
            Skip = "No SQL Server for the tests: set ConnectionStrings:PayAndPensionsTests (see docs/pay-and-pensions/README.md).";
    }
}
