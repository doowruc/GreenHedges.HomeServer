using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

/// <summary>What can be changed on an imported transaction: the rest is as the provider's export had it.</summary>
public class TransactionInput
{
    public TransactionKind Kind { get; set; }

    [StringLength(200), Display(Name = "Fund (optional)")]
    public string? Fund { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public void ApplyTo(PensionTransaction transaction)
    {
        transaction.Kind = Kind;
        transaction.Fund = string.IsNullOrWhiteSpace(Fund) ? null : Fund.Trim();
    }
}
