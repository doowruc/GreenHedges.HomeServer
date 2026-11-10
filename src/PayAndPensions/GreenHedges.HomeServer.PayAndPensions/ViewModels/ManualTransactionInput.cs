using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

/// <summary>
/// A transaction entered by hand, for a pension whose provider has no export. The amount is typed without a sign
/// (the kind gives it), except on an Other line, where money out is negative.
/// </summary>
public class ManualTransactionInput
{
    [Required(ErrorMessage = "Give the date.")]
    public DateOnly? Date { get; set; }

    public TransactionKind Kind { get; set; } = TransactionKind.Purchase;

    [StringLength(300), Display(Name = "Description (optional)")]
    public string? Description { get; set; }

    [StringLength(200)]
    public string? Fund { get; set; }

    [Range(0.000001, 1_000_000_000)]
    public decimal? Units { get; set; }

    /// <summary>In pence, as the exports and the daily prices give it.</summary>
    [Display(Name = "Price (p)"), Range(0.0001, 10_000_000)]
    public decimal? UnitPrice { get; set; }

    [Display(Name = "Value"), Range(-100_000_000, 100_000_000)]
    public decimal? Amount { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public bool IsDeal => Kind is TransactionKind.Purchase or TransactionKind.Sale;

    /// <summary>The stored value: money out negative. Call once the input is valid.</summary>
    public decimal SignedValue() => Kind switch
    {
        TransactionKind.Purchase or TransactionKind.Fee => -Math.Abs(Amount!.Value),
        TransactionKind.Other => Amount!.Value,
        _ => Math.Abs(Amount!.Value),
    };

    /// <summary>The amount as typed, from a stored value.</summary>
    public static decimal AmountOf(TransactionKind kind, decimal value) => kind == TransactionKind.Other ? value : Math.Abs(value);
}
