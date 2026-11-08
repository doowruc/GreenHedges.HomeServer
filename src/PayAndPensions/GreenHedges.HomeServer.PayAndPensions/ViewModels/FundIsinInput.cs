using System.ComponentModel.DataAnnotations;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

/// <summary>The ISIN a fund's daily prices are fetched by; blank for none.</summary>
public class FundIsinInput
{
    [Display(Name = "ISIN")]
    public string? Isin { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits; empty for a new one.</summary>
    public byte[] RowVersion { get; set; } = [];
}
