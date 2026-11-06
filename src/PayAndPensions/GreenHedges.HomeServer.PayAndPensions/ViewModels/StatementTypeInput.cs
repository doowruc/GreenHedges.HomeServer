using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class StatementTypeInput
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Has projections (the fund at retirement and annual income)")]
    public bool HasProjections { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<StatementType, StatementTypeInput>> Projection = t =>
        new StatementTypeInput { Name = t.Name, HasProjections = t.HasProjections, RowVersion = t.RowVersion };

    public void ApplyTo(StatementType type)
    {
        type.Name = Name;
        type.HasProjections = HasProjections;
    }
}
