using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class PayElementInput
{
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    public PayElementKind Kind { get; set; }

    public PayCategory Category { get; set; }

    [Display(Name = "Order"), Range(0, 999)]
    public int SortOrder { get; set; }

    [Display(Name = "Hidden (no longer on new payslips)")]
    public bool Hidden { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<PayElement, PayElementInput>> Projection = el => new PayElementInput
    {
        Name = el.Name, Kind = el.Kind, Category = el.Category, SortOrder = el.SortOrder, Hidden = el.Hidden,
        RowVersion = el.RowVersion
    };

    public void ApplyTo(PayElement element)
    {
        element.Name = Name;
        element.Kind = Kind;
        element.Category = Category;
        element.SortOrder = SortOrder;
        element.Hidden = Hidden;
    }
}
