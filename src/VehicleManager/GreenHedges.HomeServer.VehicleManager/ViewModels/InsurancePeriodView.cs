using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

/// <summary>A vehicle's time on an insurance policy (see <see cref="InsurancePolicyVehicle"/>).</summary>
public class InsurancePeriodView
{
    public int Id { get; init; }
    public int PolicyId { get; init; }
    public int VehicleId { get; init; }
    public string Make { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public decimal? Adjustment { get; init; }

    public string VehicleName => $"{Make} {Model}".Trim();

    /// <summary>E.g. "Ford Focus (AB12 CDE)".</summary>
    public string VehicleLabel => $"{VehicleName} ({RegistrationNumber})";

    public double Days => (To - From).TotalDays;

    public static readonly Expression<Func<InsurancePolicyVehicle, InsurancePeriodView>> Projection = v => new InsurancePeriodView
    {
        Id = v.Id,
        PolicyId = v.InsurancePolicyId,
        VehicleId = v.VehicleId,
        Make = v.Vehicle!.Make,
        Model = v.Vehicle.Model,
        RegistrationNumber = v.Vehicle.RegistrationNumber,
        From = v.From,
        To = v.To,
        Adjustment = v.Adjustment
    };
}
