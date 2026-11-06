using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class InsurancePolicyView
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string PolicyNumber { get; init; } = string.Empty;
    public InsuranceCover Cover { get; init; }
    public decimal Premium { get; init; }
    public decimal? Excess { get; init; }
    public string? NamedDrivers { get; init; }

    /// <summary>Owners who are named drivers on it, by name.</summary>
    public List<PolicyDriverView> Drivers { get; init; } = [];

    /// <summary>Every named driver: the owners, then the others typed in.</summary>
    public string? AllNamedDrivers => string.Join(", ", Drivers.Select(d => d.Name).Append(NamedDrivers).Where(n => !string.IsNullOrEmpty(n))) is { Length: > 0 } all ? all : null;

    public int? NoClaimsYears { get; init; }
    public bool ProtectedNoClaims { get; init; }
    public string? Notes { get; init; }

    /// <summary>The vehicles it covers, and when, in order.</summary>
    public List<InsurancePeriodView> Vehicles { get; init; } = [];

    /// <summary>In force now (set by <see cref="MarkCurrent"/>).</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Yet to start (set by <see cref="MarkCurrent"/>).</summary>
    public bool IsFuture { get; set; }

    public string CoverName => CoverNames(Cover);

    /// <summary>The premium plus any mid-term adjustments (vehicles switched or added).</summary>
    public decimal TotalCost => Premium + Vehicles.Sum(v => v.Adjustment ?? 0);

    /// <summary>How long the policy lasts, in days (a year's policy, 00:00 to 23:59 a year on, is about 365).</summary>
    public double Days => (EndDate - StartDate).TotalDays;

    /// <summary>The vehicles on the policy when it ends: the ones whose renewal it is.</summary>
    public IEnumerable<InsurancePeriodView> VehiclesAtEnd => Vehicles.Where(v => v.To == EndDate);

    /// <summary>Each vehicle once, in the order they joined, e.g. "Ford Focus (AB12 CDE), Kia Ceed (XY34 ZZZ)".</summary>
    public string VehiclesText => string.Join(", ", Vehicles.Select(v => v.VehicleLabel).Distinct());

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
        OwnerId = p.OwnerId,
        OwnerName = p.Owner!.Name,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        Provider = p.Provider,
        PolicyNumber = p.PolicyNumber,
        Cover = p.Cover,
        Premium = p.Premium,
        Excess = p.Excess,
        NamedDrivers = p.NamedDrivers,
        Drivers = p.Drivers.AsQueryable().OrderBy(d => d.Owner!.Name).Select(d => new PolicyDriverView { OwnerId = d.OwnerId, Name = d.Owner!.Name }).ToList(),
        NoClaimsYears = p.NoClaimsYears,
        ProtectedNoClaims = p.ProtectedNoClaims,
        Notes = p.Notes,
        Vehicles = p.Vehicles.AsQueryable().OrderBy(v => v.From).ThenBy(v => v.Id).Select(InsurancePeriodView.Projection).ToList()
    };
}

/// <summary>An owner who's a named driver on a policy.</summary>
public class PolicyDriverView
{
    public int OwnerId { get; init; }
    public string Name { get; init; } = string.Empty;
}
