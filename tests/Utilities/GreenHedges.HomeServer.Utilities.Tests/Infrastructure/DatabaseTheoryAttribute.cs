namespace GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

/// <summary>A theory that needs the database; skipped when no SQL Server for the tests is configured.</summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (!TestDatabase.IsConfigured)
            Skip = "No SQL Server for the tests: set ConnectionStrings:UtilitiesTests (see docs/utilities/README.md).";
    }
}
