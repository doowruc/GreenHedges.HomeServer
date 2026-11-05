using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

/// <summary>How and when a vehicle was disposed of (<c>Pages/Vehicles/Dispose</c>).</summary>
public class DisposalInput : IValidatableObject
{
    [Required(ErrorMessage = "Choose how it went.")]
    [Display(Name = "How it went")]
    public DisposalMethod? Method { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Date")]
    public DateOnly? Date { get; set; }

    /// <summary>A sale price, part-exchange value, insurance payout or scrap value (named for the method on the form).</summary>
    [Range(0, 9_999_999.99)]
    [Display(Name = "Price (£)")]
    public decimal? Price { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Method is { } method && !Enum.IsDefined(method))
            yield return new ValidationResult("Choose how it went.", [nameof(Method)]);
    }

    public void ApplyTo(Vehicle vehicle)
    {
        vehicle.DisposalMethod = Method;
        vehicle.DisposedDate = Date;
        vehicle.DisposalPrice = Price;
    }
}
