using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>One amount on a payslip: how much of one pay element. Can be negative, e.g. back pay taken back.</summary>
public class PayslipLine : AuditedEntity
{
    public int Id { get; set; }

    public int PayslipId { get; set; }
    public Payslip? Payslip { get; set; }

    public int PayElementId { get; set; }
    public PayElement? PayElement { get; set; }

    public decimal Amount { get; set; }
}
