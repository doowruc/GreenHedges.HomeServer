using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

/// <summary>A vehicle on an insurance policy, and when (see <see cref="InsurancePolicyVehicle"/>).</summary>
public class InsurancePeriodInput : IValidatableObject
{
    [Required]
    [Display(Name = "Vehicle")]
    public int? VehicleId { get; set; }

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Covered from")]
    public DateTime? From { get; set; }

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Covered to")]
    public DateTime? To { get; set; }

    [Range(-999_999.99, 999_999.99)]
    [Display(Name = "Adjustment (£)")]
    public decimal? Adjustment { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is { } from && To is { } to && to <= from)
            yield return new ValidationResult("The cover must end after it starts.", [nameof(To)]);
    }

    /// <summary>Errors if the cover isn't within the policy's dates (none if it is).</summary>
    public IEnumerable<string> OutsidePolicy(DateTime policyStart, DateTime policyEnd)
    {
        if (InsurancePolicyInput.ToMinute(From!.Value) < policyStart)
            yield return $"The cover can't start before the policy does ({policyStart:dd/MM/yyyy HH:mm}).";
        if (InsurancePolicyInput.ToMinute(To!.Value) > policyEnd)
            yield return $"The cover can't end after the policy does ({policyEnd:dd/MM/yyyy HH:mm}).";
    }

    public static readonly Expression<Func<InsurancePolicyVehicle, InsurancePeriodInput>> Projection = v => new InsurancePeriodInput
    {
        VehicleId = v.VehicleId,
        From = v.From,
        To = v.To,
        Adjustment = v.Adjustment,
        RowVersion = v.RowVersion
    };

    /// <summary>Copies the fields across, dropping seconds. Call only once the input is valid and its vehicle is the user's.</summary>
    public void ApplyTo(InsurancePolicyVehicle period)
    {
        period.VehicleId = VehicleId!.Value;
        period.From = InsurancePolicyInput.ToMinute(From!.Value);
        period.To = InsurancePolicyInput.ToMinute(To!.Value);
        period.Adjustment = Adjustment is 0 ? null : Adjustment;
    }
}
