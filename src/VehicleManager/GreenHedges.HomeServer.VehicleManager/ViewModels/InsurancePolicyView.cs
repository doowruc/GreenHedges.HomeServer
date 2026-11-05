using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class InsurancePolicyView
{
    public int Id { get; init; }
    public int VehicleId { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string PolicyNumber { get; init; } = string.Empty;
    public InsuranceCover Cover { get; init; }
    public decimal Premium { get; init; }
    public decimal? Excess { get; init; }
    public string? NamedDrivers { get; init; }
    public string? Notes { get; init; }

    /// <summary>In force now (set by <see cref="MarkCurrent"/>).</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Yet to start (set by <see cref="MarkCurrent"/>).</summary>
    public bool IsFuture { get; set; }

    public string CoverName => CoverNames(Cover);

    /// <summary>How long the policy lasts, in days (a year's policy, 00:00 to 23:59 a year on, is about 365).</summary>
    public double Days => (EndDate - StartDate).TotalDays;

    /// <summary>The premium for a year at the same rate, so policies of different lengths can be compared.</summary>
    public decimal YearlyPremium => Days <= 0 ? Premium : Math.Round(Premium * (decimal)(DaysPerYear / Days), 2);

    /// <summary>Not about a year long (outside 360–370 days), so its yearly premium differs from what it cost.</summary>
    public bool IsPartYear => Days is < 360 or > 370;

    /// <summary>Policies 00:00 to 23:59 a year on are 365 days less a minute; allow for leap years.</summary>
    private const double DaysPerYear = 365.25;

    public static string CoverNames(InsuranceCover cover) => cover switch
    {
        InsuranceCover.Comprehensive => "Comprehensive",
        InsuranceCover.ThirdPartyFireAndTheft => "Third party, fire and theft",
        InsuranceCover.ThirdPartyOnly => "Third party only",
        _ => cover.ToString()
    };

    /// <summary>
    /// The last day a policy ending at <paramref name="end"/> covers: its date, or the day before for a policy ending at
    /// midnight (00:00 is the start of that day, so cover has already ended). That's the renewal date reminded about.
    /// </summary>
    public static DateOnly LastDay(DateTime end) => DateOnly.FromDateTime(end.TimeOfDay == TimeSpan.Zero ? end.AddDays(-1) : end);

    /// <summary>Marks which policies are in force at <paramref name="ukNow"/> and which are yet to start.</summary>
    public static void MarkCurrent(IEnumerable<InsurancePolicyView> policies, DateTime ukNow)
    {
        foreach (var p in policies)
        {
            p.IsCurrent = p.StartDate <= ukNow && ukNow < p.EndDate;
            p.IsFuture = p.StartDate > ukNow;
        }
    }

    public static readonly Expression<Func<InsurancePolicy, InsurancePolicyView>> Projection = p => new InsurancePolicyView
    {
        Id = p.Id,
        VehicleId = p.VehicleId,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        Provider = p.Provider,
        PolicyNumber = p.PolicyNumber,
        Cover = p.Cover,
        Premium = p.Premium,
        Excess = p.Excess,
        NamedDrivers = p.NamedDrivers,
        Notes = p.Notes
    };
}
