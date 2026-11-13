using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Presents.Models;

namespace GreenHedges.HomeServer.Presents.ViewModels;

public class PurchaseInput
{
    [Required(ErrorMessage = "Choose who it's for."), Display(Name = "For")]
    public int? ChildId { get; set; }

    [Required(ErrorMessage = "Enter what it is."), StringLength(200), Display(Name = "Item")]
    public string Item { get; set; } = string.Empty;

    [StringLength(100), Display(Name = "Shop (optional)")]
    public string? Shop { get; set; }

    /// <summary>When it was bought, if known. It doesn't decide the year: the Christmas does.</summary>
    [Display(Name = "Bought on (optional)")]
    public DateOnly? Date { get; set; }

    /// <summary>The full price of one, before any split.</summary>
    [Required(ErrorMessage = "Enter the price."), Range(0.01, 99_999_999.99, ErrorMessage = "The price must be between £0.01 and £99,999,999.99.")]
    [Display(Name = "Price")]
    public decimal? Price { get; set; }

    [Range(1, 999, ErrorMessage = "The quantity must be between 1 and 999."), Display(Name = "Quantity")]
    public int Quantity { get; set; } = 1;

    [Display(Name = "Split with Mum")]
    public bool SplitWithMum { get; set; }

    [Display(Name = "Split with birthday")]
    public bool SplitWithBirthday { get; set; }

    /// <summary>The Christmas it belongs to. Only the edit form asks; adding uses the page's year.</summary>
    [Range(2000, 2999, ErrorMessage = "The year must be between 2000 and 2999."), Display(Name = "Christmas")]
    public int Year { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static PurchaseInput From(Purchase p, int year) => new()
    {
        ChildId = p.ChildId, Item = p.Item, Shop = p.Shop, Date = p.Date, Price = p.Price, Quantity = p.Quantity,
        SplitWithMum = p.SplitWithMum, SplitWithBirthday = p.SplitWithBirthday, Year = year, RowVersion = p.RowVersion
    };

    /// <summary>Copies everything but the Christmas (and the row version) to the entity.</summary>
    public void ApplyTo(Purchase purchase)
    {
        purchase.ChildId = ChildId!.Value;
        purchase.Item = Item;
        purchase.Shop = Shop;
        purchase.Date = Date;
        purchase.Price = Price!.Value;
        purchase.Quantity = Quantity;
        purchase.SplitWithMum = SplitWithMum;
        purchase.SplitWithBirthday = SplitWithBirthday;
    }
}
