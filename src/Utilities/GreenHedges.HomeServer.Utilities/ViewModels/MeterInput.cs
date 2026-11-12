using System.ComponentModel.DataAnnotations;

namespace GreenHedges.HomeServer.Utilities.ViewModels;

public class MeterInput
{
    /// <summary>Required for a replacement meter; a utility's first meter has none.</summary>
    [Display(Name = "Installed on")]
    public DateOnly? InstalledOn { get; set; }

    /// <summary>As <see cref="InstalledOn"/>.</summary>
    [Range(0, 999_999_999, ErrorMessage = "The start reading must be between 0 and 999,999,999.")]
    [Display(Name = "Start reading")]
    public decimal? StartReading { get; set; }

    [StringLength(50), Display(Name = "Serial number (optional)")]
    public string? SerialNumber { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public string? TidySerialNumber => string.IsNullOrWhiteSpace(SerialNumber) ? null : SerialNumber.Trim();
}
