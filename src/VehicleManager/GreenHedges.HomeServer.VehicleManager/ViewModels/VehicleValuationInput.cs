using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class VehicleValuationInput : IValidatableObject
{
    [Required]
    [DataType(DataType.Date)]
    public DateOnly? Date { get; set; }

    [Required, Range(0, 9_999_999)]
    public int? Mileage { get; set; }

    [Required, Range(0.01, 9_999_999.99)]
    [Display(Name = "Value (£)")]
    public decimal? Value { get; set; }

    [Required(ErrorMessage = "Choose the type of value.")]
    [Display(Name = "Value type")]
    public ValuationType? Type { get; set; }

    /// <summary>Optional: only when the site gives several values for the type.</summary>
    public ValuationGrade? Grade { get; set; }

    [Required(ErrorMessage = "Choose where the value came from.")]
    public ValuationSource? Source { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Date > DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
            yield return new ValidationResult("The date can't be in the future.", [nameof(Date)]);
        if (Type is { } type && !Enum.IsDefined(type))
            yield return new ValidationResult("Choose the type of value.", [nameof(Type)]);
        if (Grade is { } grade && !Enum.IsDefined(grade))
            yield return new ValidationResult("Choose a grade, or none.", [nameof(Grade)]);
        if (Source is { } source && !Enum.IsDefined(source))
            yield return new ValidationResult("Choose where the value came from.", [nameof(Source)]);
    }

    public static readonly Expression<Func<VehicleValuation, VehicleValuationInput>> Projection = v => new VehicleValuationInput
    {
        Date = v.Date,
        Mileage = v.Mileage,
        Value = v.Value,
        Type = v.Type,
        Grade = v.Grade,
        Source = v.Source,
        RowVersion = v.RowVersion
    };

    /// <summary>Copies the fields across. Call only once the input is valid.</summary>
    public void ApplyTo(VehicleValuation valuation)
    {
        valuation.Date = Date!.Value;
        valuation.Mileage = Mileage!.Value;
        valuation.Value = Value!.Value;
        valuation.Type = Type!.Value;
        valuation.Grade = Grade;
        valuation.Source = Source!.Value;
    }
}
