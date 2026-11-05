using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class ClaimView
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public int VehicleId { get; init; }
    public string Make { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public int? PolicyId { get; init; }
    public string? Provider { get; init; }
    public string? PolicyNumber { get; init; }
    public DateOnly Date { get; init; }
    public ClaimType Type { get; init; }
    public ClaimFault Fault { get; init; }
    public decimal TotalValue { get; init; }

    public string VehicleName => $"{Make} {Model}".Trim();
    public string? PolicyLabel => PolicyId is null ? null : $"{Provider} {PolicyNumber}";
    public string TypeName => Type.ToString();
    public string FaultName => FaultNames(Fault);

    /// <summary>Who was at fault, for the form and the claims table.</summary>
    public static string FaultNames(ClaimFault fault) => fault switch
    {
        ClaimFault.OtherParty => "Other party",
        ClaimFault.Both => "Both (split)",
        _ => "Owner"
    };

    /// <summary>The badge colour for who was at fault: the owner's own fault counts against them most.</summary>
    public string FaultBadge => Fault switch
    {
        ClaimFault.Owner => "text-bg-danger",
        ClaimFault.Both => "text-bg-warning",
        _ => "text-bg-success"
    };

    public static readonly Expression<Func<Claim, ClaimView>> Projection = c => new ClaimView
    {
        Id = c.Id,
        OwnerId = c.OwnerId,
        OwnerName = c.Owner!.Name,
        VehicleId = c.VehicleId,
        Make = c.Vehicle!.Make,
        Model = c.Vehicle.Model,
        RegistrationNumber = c.Vehicle.RegistrationNumber,
        PolicyId = c.InsurancePolicyId,
        Provider = c.InsurancePolicy == null ? null : c.InsurancePolicy.Provider,
        PolicyNumber = c.InsurancePolicy == null ? null : c.InsurancePolicy.PolicyNumber,
        Date = c.Date,
        Type = c.Type,
        Fault = c.Fault,
        TotalValue = c.TotalValue
    };
}
