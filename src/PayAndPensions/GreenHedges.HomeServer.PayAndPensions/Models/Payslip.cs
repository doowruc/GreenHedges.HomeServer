using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>One payslip: a person's pay for one month of a tax year, from one employer.</summary>
public class Payslip : AuditedEntity
{
    public int Id { get; set; }

    public int PersonId { get; set; }
    public Person? Person { get; set; }

    public int EmployerId { get; set; }
    public Employer? Employer { get; set; }

    /// <summary>The year the tax year starts in: 2026 is 2026-27 (6 April 2026 to 5 April 2027).</summary>
    public int TaxYear { get; set; }

    /// <summary>The month of the tax year, 1 (April) to 12 (March).</summary>
    public int TaxMonth { get; set; }

    public DateOnly PayDate { get; set; }

    /// <summary>When the month's pension contributions reached the pension, where they're paid in separately.</summary>
    public DateOnly? PensionReceivedDate { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public List<PayslipLine> Lines { get; set; } = [];
}
