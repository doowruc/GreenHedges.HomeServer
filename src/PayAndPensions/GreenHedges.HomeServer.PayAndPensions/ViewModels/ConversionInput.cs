using System.ComponentModel.DataAnnotations;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

/// <summary>A fund conversion to record: all of one fund's units, on a date, became so many of another's.</summary>
public class ConversionInput
{
    [Required(ErrorMessage = "Enter the date of the conversion.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Choose the fund converted from."), Display(Name = "Converted from")]
    public string? FromFund { get; set; }

    [Required(ErrorMessage = "Enter the fund converted to."), StringLength(200), Display(Name = "Converted to")]
    public string? ToFund { get; set; }

    [Required(ErrorMessage = "Enter the units received."), Display(Name = "Units received")]
    [Range(typeof(decimal), "0.000001", "9999999999999", ErrorMessage = "The units received must be more than 0.")]
    public decimal? Units { get; set; }
}
