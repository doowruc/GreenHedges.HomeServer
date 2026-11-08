using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>A person's pension with a provider, e.g. a workplace pension or a SIPP.</summary>
public class Pension : AuditedEntity
{
    public int Id { get; set; }

    public int PersonId { get; set; }
    public Person? Person { get; set; }

    public int PensionProviderId { get; set; }
    public PensionProvider? PensionProvider { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    public string? PolicyNumber { get; set; }

    public PensionType Type { get; set; }

    /// <summary>When it started: statements' days-since-start figures count from it, and the first one's change from 0.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>The payslips whose pension lines go to it.</summary>
    public List<Payslip> Payslips { get; set; } = [];

    public List<StatementType> StatementTypes { get; set; } = [];

    public List<PensionTransaction> Transactions { get; set; } = [];

    public List<PensionFund> Funds { get; set; } = [];
}

public enum PensionType
{
    Workplace,
    [Display(Name = "SIPP")] Sipp,
    [Display(Name = "Personal pension")] PersonalPension,
    Stakeholder,
    Other,
}
