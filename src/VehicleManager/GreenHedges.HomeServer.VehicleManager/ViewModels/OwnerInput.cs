using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public partial class OwnerInput : IValidatableObject
{
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Licence first issued")]
    public DateOnly? LicenceIssueDate { get; set; }

    /// <summary>Upper case with no spaces once normalised (<see cref="NormaliseDriverNumber"/>).</summary>
    [StringLength(30)]
    [Display(Name = "Driver number")]
    public string? DriverNumber { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// A GB driver number: 5 characters of surname (padded with 9s), 6 digits of birth date, 2 initials (or 9s), a digit
    /// and 2 check letters, e.g. MORGA753116SM9IJ.
    /// </summary>
    [GeneratedRegex("^[A-Z9]{5}[0-9]{6}[A-Z9]{2}[0-9][A-Z]{2}$")]
    public static partial Regex DriverNumberPattern();

    /// <summary>Upper case, without spaces (it's often written in groups); null if blank.</summary>
    public static string? NormaliseDriverNumber(string? number) =>
        string.IsNullOrWhiteSpace(number) ? null : string.Concat(number.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NormaliseDriverNumber(DriverNumber) is { } number && !DriverNumberPattern().IsMatch(number))
            yield return new ValidationResult("That isn't a driver number: it's 16 letters and digits, e.g. MORGA753116SM9IJ.", [nameof(DriverNumber)]);
        if (LicenceIssueDate > DateOnly.FromDateTime(DateTime.Today))
            yield return new ValidationResult("The licence can't be issued in the future.", [nameof(LicenceIssueDate)]);
    }

    public static readonly Expression<Func<Owner, OwnerInput>> Projection = o => new OwnerInput
    {
        Name = o.Name, LicenceIssueDate = o.LicenceIssueDate, DriverNumber = o.DriverNumber, RowVersion = o.RowVersion
    };

    public void ApplyTo(Owner owner)
    {
        owner.Name = Name;
        owner.LicenceIssueDate = LicenceIssueDate;
        owner.DriverNumber = NormaliseDriverNumber(DriverNumber);
    }
}
