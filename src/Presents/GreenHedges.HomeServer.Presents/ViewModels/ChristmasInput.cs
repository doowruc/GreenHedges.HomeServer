using System.ComponentModel.DataAnnotations;

namespace GreenHedges.HomeServer.Presents.ViewModels;

public class ChristmasInput
{
    /// <summary>What each child's Christmas should come to; none to only balance the children.</summary>
    [Range(0.01, 99_999_999.99, ErrorMessage = "The budget must be between £0.01 and £99,999,999.99.")]
    [Display(Name = "Budget per child (optional)")]
    public decimal? Budget { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];
}
