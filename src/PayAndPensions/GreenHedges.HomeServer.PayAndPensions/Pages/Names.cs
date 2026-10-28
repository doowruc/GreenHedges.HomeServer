namespace GreenHedges.HomeServer.PayAndPensions.Pages;

internal static class Names
{
    /// <summary>A name as typed, with spaces trimmed and runs of spaces made one.</summary>
    public static string Normalise(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
