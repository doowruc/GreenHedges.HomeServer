using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Services;

/// <summary>The <c>{utility:utility}</c> route constraint: a utility's name (Electricity, Gas, Water), in any case.</summary>
public sealed class UtilityRouteConstraint : IRouteConstraint
{
    public const string Name = "utility";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection) =>
        TryParse(Convert.ToString(values[routeKey]), out _);

    /// <summary>By name only: numbers ("0") aren't utilities.</summary>
    public static bool TryParse(string? text, out UtilityType utility)
    {
        utility = default;
        return !string.IsNullOrEmpty(text) && char.IsLetter(text[0])
            && Enum.TryParse(text, ignoreCase: true, out utility) && Enum.IsDefined(utility);
    }
}
