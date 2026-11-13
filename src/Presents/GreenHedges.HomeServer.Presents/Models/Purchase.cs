using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.Presents.Models;

/// <summary>
/// A present bought for a child one Christmas. It keeps the full price and the splits; what it counts as (the share:
/// price × quantity, halved for each split) is worked out.
/// </summary>
public class Purchase : AuditedEntity
{
    public int Id { get; set; }

    public int ChristmasId { get; set; }
    public Christmas? Christmas { get; set; }

    public int ChildId { get; set; }
    public Child? Child { get; set; }

    [Required, StringLength(200)]
    public string Item { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Shop { get; set; }

    /// <summary>When it was bought, if known: often months before Christmas, and not what decides the year.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>The full price of one, before any split.</summary>
    public decimal Price { get; set; }

    public int Quantity { get; set; } = 1;

    /// <summary>Shared with my mum: it counts half.</summary>
    public bool SplitWithMum { get; set; }

    /// <summary>Shared with the child's birthday: it counts half (a quarter with <see cref="SplitWithMum"/>).</summary>
    public bool SplitWithBirthday { get; set; }
}
