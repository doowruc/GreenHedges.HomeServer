using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>
/// One kind of line on a person's payslips, e.g. Salary, Overtime, Tax or Employer pension: a column of their
/// spreadsheet. Each person has their own, as payslips differ between employers.
/// </summary>
public class PayElement : AuditedEntity
{
    public int Id { get; set; }

    public int PersonId { get; set; }
    public Person? Person { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether it adds to gross pay, comes off it, or is the employer's (outside gross and net).</summary>
    public PayElementKind Kind { get; set; }

    /// <summary>What it is, for the calculations (annual salary, pension %, bonus %, tax-year totals).</summary>
    public PayCategory Category { get; set; }

    /// <summary>Order on forms and in tables, lowest first.</summary>
    public int SortOrder { get; set; }

    /// <summary>No longer used: kept for old payslips, left off new ones.</summary>
    public bool Hidden { get; set; }

    public List<PayslipLine> Lines { get; set; } = [];
}

public enum PayElementKind
{
    Earning,
    Deduction,
    [Display(Name = "Employer contribution")] EmployerContribution,
}

public enum PayCategory
{
    Salary,
    Bonus,
    Allowance,
    Overtime,
    [Display(Name = "Back pay")] BackPay,
    Tax,
    [Display(Name = "National Insurance")] NationalInsurance,
    Pension,
    Other,
}
