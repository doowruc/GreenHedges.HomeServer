using System.ComponentModel.DataAnnotations;

namespace GreenHedges.HomeServer.Utilities.ViewModels;

public class ReadingInput
{
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Enter the reading."), Range(0, 999_999_999, ErrorMessage = "The reading must be between 0 and 999,999,999.")]
    [Display(Name = "Reading")]
    public decimal? Value { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];
}
