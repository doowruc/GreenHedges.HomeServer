using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PensionProviders;

internal static class PensionProviderRules
{
    /// <summary>
    /// Normalises the input and checks it: the name unique among the user's providers, ignoring case
    /// (<paramref name="id"/> is null for a new provider), and the website, if any, an http(s) address. A website typed
    /// without its https:// gets it.
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int? id, PensionProviderInput provider, ModelStateDictionary state)
    {
        provider.Name = Names.Normalise(provider.Name);
        var name = provider.Name.ToLower();
        if (await db.PensionProviders.AnyAsync(pp => pp.Id != id && pp.Name.ToLower() == name))
            state.AddModelError("Provider.Name", "There's already a provider with this name.");

        provider.Website = Website(provider.Website);
        if (provider.Website is { } website &&
            !(Uri.TryCreate(website, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.')))
            state.AddModelError("Provider.Website", "Enter a website address, e.g. https://www.example.com.");
    }

    private static string? Website(string? typed)
    {
        var website = typed?.Trim();
        if (string.IsNullOrEmpty(website)) return null;
        return website.Contains("://") ? website : "https://" + website;
    }
}
