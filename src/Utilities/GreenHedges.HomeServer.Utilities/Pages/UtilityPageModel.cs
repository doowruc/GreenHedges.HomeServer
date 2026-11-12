using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;

namespace GreenHedges.HomeServer.Utilities.Pages;

/// <summary>Base for a utility's pages, at <c>/{utility}/…</c> (/Electricity, /Gas, /Water).</summary>
public abstract class UtilityPageModel(AppDbContext db) : PageModel
{
    protected AppDbContext Db => db;

    /// <summary>The utility's name from the address.</summary>
    [BindProperty(SupportsGet = true)]
    public string Utility { get; set; } = string.Empty;

    public UtilityType Type { get; private set; }

    /// <summary>The utility's name in lower case, for sentences.</summary>
    public string Lower => Type.ToString().ToLowerInvariant();

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Reads <see cref="Type"/> from the address. False if it isn't a utility.</summary>
    protected bool LoadUtility()
    {
        if (!UtilityRouteConstraint.TryParse(Utility, out var type)) return false;
        Type = type;
        Utility = type.ToString();
        return true;
    }

    protected IActionResult RedirectToUtility() => RedirectToPage("/Readings/Index", new { utility = Type.ToString() });

    /// <summary>More than three decimal places: more than a reading keeps.</summary>
    protected static bool TooPrecise(decimal value) => decimal.Round(value, 3) != value;
}
