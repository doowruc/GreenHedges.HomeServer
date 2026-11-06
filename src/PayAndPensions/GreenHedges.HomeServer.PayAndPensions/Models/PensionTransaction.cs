using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>
/// One line of a pension's transaction history, as the provider's export has it: money in (positive) or out
/// (negative), and for a fund purchase or sale its units and price.
/// </summary>
public class PensionTransaction : AuditedEntity
{
    public int Id { get; set; }

    public int PensionId { get; set; }
    public Pension? Pension { get; set; }

    public DateOnly TradeDate { get; set; }

    public DateOnly? SettleDate { get; set; }

    [Required, StringLength(50)]
    public string Reference { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Worked out on import from the reference and description; changeable.</summary>
    public TransactionKind Kind { get; set; }

    /// <summary>The fund bought or sold, from the description; changeable.</summary>
    [StringLength(200)]
    public string? Fund { get; set; }

    /// <summary>The price per unit, in pence, as the export gives it.</summary>
    public decimal? UnitPrice { get; set; }

    public decimal? Quantity { get; set; }

    /// <summary>In pounds: money in is positive, out negative.</summary>
    public decimal Value { get; set; }
}

public enum TransactionKind
{
    Contribution,
    [Display(Name = "Transfer in")] TransferIn,
    Interest,
    Fee,
    Rebate,
    Purchase,
    Sale,
    Other,
}
