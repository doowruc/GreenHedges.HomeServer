using System.Text.Json;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// What each vehicle's insurance cost: a policy's premium shared between its vehicles by their time on it (a car on it
/// for 8 months of 12 gets two thirds; two cars on it all year, half each), plus each one's own mid-term adjustment.
/// </summary>
public static class InsuranceCosts
{
    /// <summary>Allows for leap years.</summary>
    public const double DaysPerYear = 365.25;

    /// <summary>This vehicle period's share of its policy's cost.</summary>
    public static decimal Share(InsurancePolicyView policy, InsurancePeriodView period)
    {
        var total = policy.Vehicles.Sum(v => (v.To - v.From).TotalMinutes);
        var portion = total <= 0 ? 0 : (decimal)((period.To - period.From).TotalMinutes / total);
        return Math.Round(policy.Premium * portion + (period.Adjustment ?? 0), 2);
    }

    /// <summary>A cost for <paramref name="days"/> as the same rate for a year.</summary>
    public static decimal Yearly(decimal cost, double days) => days <= 0 ? cost : Math.Round(cost * (decimal)(DaysPerYear / days), 2);

    /// <summary>
    /// The premium chart's data (for wwwroot/js/premium-chart.js): one series per vehicle, oldest first, each point a
    /// vehicle period at its start with its share and the same rate for a year. Null with fewer than two points to draw.
    /// </summary>
    public static string? ChartJson(IEnumerable<InsurancePolicyView> policies, int? onlyVehicleId = null)
    {
        var points = policies
            .SelectMany(p => p.Vehicles.Select(v => (Policy: p, Period: v)))
            .Where(x => onlyVehicleId is null || x.Period.VehicleId == onlyVehicleId)
            .ToList();
        if (points.Count < 2) return null;

        return JsonSerializer.Serialize(points
            .GroupBy(x => x.Period.VehicleId)
            .Select(g => g.OrderBy(x => x.Period.From).ToList())
            .OrderBy(g => g[0].Period.From)
            .Select(g => new
            {
                vehicle = g[0].Period.VehicleLabel,
                points = g.Select(x =>
                {
                    var cost = Share(x.Policy, x.Period);
                    return new
                    {
                        start = x.Period.From.ToString("yyyy-MM-ddTHH:mm"),
                        end = x.Period.To.ToString("yyyy-MM-ddTHH:mm"),
                        provider = x.Policy.Provider,
                        cost,
                        yearly = Yearly(cost, x.Period.Days),
                        days = Math.Round(x.Period.Days),
                        policyId = x.Policy.Id
                    };
                })
            }));
    }
}
