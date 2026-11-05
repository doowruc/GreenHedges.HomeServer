using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class ClaimInput : IValidatableObject
{
    [Required(ErrorMessage = "Choose the vehicle.")]
    [Display(Name = "Vehicle")]
    public int? VehicleId { get; set; }

    /// <summary>Optional: an older claim may predate the policies recorded.</summary>
    [Display(Name = "Insurance policy")]
    public int? InsurancePolicyId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly? Date { get; set; }

    public ClaimType Type { get; set; } = ClaimType.Accident;

    [Required(ErrorMessage = "Choose who was at fault.")]
    [Display(Name = "At fault")]
    public ClaimFault? Fault { get; set; }

    [Required, Range(0, 9_999_999.99)]
    [Display(Name = "Total value (£)")]
    public decimal? TotalValue { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Date > DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
            yield return new ValidationResult("The date can't be in the future.", [nameof(Date)]);
        if (!Enum.IsDefined(Type))
            yield return new ValidationResult("Choose the type of claim.", [nameof(Type)]);
        if (Fault is { } fault && !Enum.IsDefined(fault))
            yield return new ValidationResult("Choose who was at fault.", [nameof(Fault)]);
    }

    public static readonly Expression<Func<Claim, ClaimInput>> Projection = c => new ClaimInput
    {
        VehicleId = c.VehicleId,
        InsurancePolicyId = c.InsurancePolicyId,
        Date = c.Date,
        Type = c.Type,
        Fault = c.Fault,
        TotalValue = c.TotalValue,
        RowVersion = c.RowVersion
    };

    /// <summary>Copies the fields across. Call only once the input is valid and its vehicle and policy are checked.</summary>
    public void ApplyTo(Claim claim)
    {
        claim.VehicleId = VehicleId!.Value;
        claim.InsurancePolicyId = InsurancePolicyId;
        claim.Date = Date!.Value;
        claim.Type = Type;
        claim.Fault = Fault!.Value;
        claim.TotalValue = TotalValue!.Value;
    }
}
