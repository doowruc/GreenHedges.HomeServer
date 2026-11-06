using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class StatementInput
{
    /// <summary>Comes back from the form, so it's checked against the pension's own types.</summary>
    [Display(Name = "Statement type")]
    public int StatementTypeId { get; set; }

    public DateOnly Date { get; set; }

    [StringLength(50), Display(Name = "Label (optional)")]
    public string? Label { get; set; }

    [Required(ErrorMessage = "Enter the value."), Range(-100_000_000, 100_000_000)]
    public decimal? Value { get; set; }

    [Display(Name = "Projected fund at retirement"), Range(0, 100_000_000)]
    public decimal? ProjectedFund { get; set; }

    [Display(Name = "Projected annual income"), Range(0, 10_000_000)]
    public decimal? ProjectedAnnualIncome { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public void ApplyTo(PensionStatement statement)
    {
        statement.StatementTypeId = StatementTypeId;
        statement.Date = Date;
        statement.Label = Label;
        statement.Value = Value!.Value;
        statement.ProjectedFund = ProjectedFund;
        statement.ProjectedAnnualIncome = ProjectedAnnualIncome;
        statement.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
    }
}
