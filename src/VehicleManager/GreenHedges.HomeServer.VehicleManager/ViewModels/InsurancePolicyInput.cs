using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class InsurancePolicyInput : IValidatableObject
{
    /// <summary>The format of the start and end in the form's datetime-local inputs (to the minute).</summary>
    public const string InputFormat = "{0:yyyy-MM-ddTHH:mm}";

    [Required]
    [Display(Name = "Owner (policyholder)")]
    public int? OwnerId { get; set; }

    /// <summary>A new policy's first vehicle, covered for the whole policy (not on edit: see the policy's vehicles).</summary>
    [Display(Name = "Vehicle")]
    public int? VehicleId { get; set; }

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Starts")]
    public DateTime? StartDate { get; set; }

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Ends")]
    public DateTime? EndDate { get; set; }

    [Required, StringLength(InsurancePolicy.MaxProviderLength)]
    [Display(Name = "Insurer")]
    public string Provider { get; set; } = string.Empty;

    [Required, StringLength(InsurancePolicy.MaxPolicyNumberLength)]
    [Display(Name = "Policy number")]
    public string PolicyNumber { get; set; } = string.Empty;

    public InsuranceCover Cover { get; set; } = InsuranceCover.Comprehensive;

    [Required, Range(0.01, 999_999.99)]
    [Display(Name = "Premium (£)")]
    public decimal? Premium { get; set; }

    [Range(0, 999_999.99)]
    [Display(Name = "Total excess (£)")]
    public decimal? Excess { get; set; }

    /// <summary>Owners who are named drivers (not the policyholder): their claims and licence go on its pre-renewal.</summary>
    [Display(Name = "Named drivers")]
    public List<int> DriverIds { get; set; } = [];

    [StringLength(InsurancePolicy.MaxNamedDriversLength)]
    [Display(Name = "Other named drivers")]
    public string? NamedDrivers { get; set; }

    [Range(0, InsurancePolicy.MaxNoClaimsYears)]
    [Display(Name = "No claims bonus (years)")]
    public int? NoClaimsYears { get; set; }

    [Display(Name = "Protected no claims bonus")]
    public bool ProtectedNoClaims { get; set; }

    [StringLength(InsurancePolicy.MaxNotesLength)]
    public string? Notes { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate is { } start && EndDate is { } end && end <= start)
            yield return new ValidationResult("The policy must end after it starts.", [nameof(EndDate)]);
        if (!Enum.IsDefined(Cover))
            yield return new ValidationResult("Choose the cover.", [nameof(Cover)]);
    }

    public static readonly Expression<Func<InsurancePolicy, InsurancePolicyInput>> Projection = p => new InsurancePolicyInput
    {
        OwnerId = p.OwnerId,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        Provider = p.Provider,
        PolicyNumber = p.PolicyNumber,
        Cover = p.Cover,
        Premium = p.Premium,
        Excess = p.Excess,
        NamedDrivers = p.NamedDrivers,
        DriverIds = p.Drivers.Select(d => d.OwnerId).ToList(),
        NoClaimsYears = p.NoClaimsYears,
        ProtectedNoClaims = p.ProtectedNoClaims,
        Notes = p.Notes,
        RowVersion = p.RowVersion
    };

    /// <summary>
    /// Copies the policy's fields across, tidying spaces and dropping seconds. Call only once the input is valid and its
    /// owner is the user's. Not the vehicle: see <see cref="InsurancePolicyVehicle"/>.
    /// </summary>
    public void ApplyTo(InsurancePolicy policy)
    {
        policy.OwnerId = OwnerId!.Value;
        policy.StartDate = ToMinute(StartDate!.Value);
        policy.EndDate = ToMinute(EndDate!.Value);
        policy.Provider = FuelStops.NormaliseName(Provider);
        policy.PolicyNumber = PolicyNumber.Trim();
        policy.Cover = Cover;
        policy.Premium = Premium!.Value;
        policy.Excess = Excess;
        policy.NamedDrivers = string.IsNullOrWhiteSpace(NamedDrivers) ? null : FuelStops.NormaliseName(NamedDrivers);
        policy.NoClaimsYears = NoClaimsYears;
        policy.ProtectedNoClaims = ProtectedNoClaims;
        policy.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
    }

    /// <summary>UK wall-clock time to the minute: no seconds, and no time zone.</summary>
    public static DateTime ToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
}
