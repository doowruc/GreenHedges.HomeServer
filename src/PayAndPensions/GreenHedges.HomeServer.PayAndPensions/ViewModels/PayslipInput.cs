using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class PayslipInput
{
    [Display(Name = "Employer")]
    public int EmployerId { get; set; }

    [Display(Name = "Tax year")]
    public int TaxYear { get; set; }

    [Display(Name = "Tax month"), Range(1, 12)]
    public int TaxMonth { get; set; }

    [Display(Name = "Pay date")]
    public DateOnly PayDate { get; set; }

    [Display(Name = "Pension received")]
    public DateOnly? PensionReceivedDate { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>One per pay element shown on the form, in the form's order; blank amounts aren't saved.</summary>
    public List<PayslipLineInput> Lines { get; set; } = [];

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Copies everything but the lines, which need the person's pay elements checked first.</summary>
    public void ApplyTo(Payslip payslip)
    {
        payslip.EmployerId = EmployerId;
        payslip.TaxYear = TaxYear;
        payslip.TaxMonth = TaxMonth;
        payslip.PayDate = PayDate;
        payslip.PensionReceivedDate = PensionReceivedDate;
        payslip.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
    }
}

public class PayslipLineInput
{
    public int PayElementId { get; set; }

    [Range(-1_000_000, 1_000_000)]
    public decimal? Amount { get; set; }
}
