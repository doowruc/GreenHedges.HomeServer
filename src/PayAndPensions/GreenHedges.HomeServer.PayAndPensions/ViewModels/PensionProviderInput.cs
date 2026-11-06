using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class PensionProviderInput
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200), Display(Name = "Website (optional)")]
    public string? Website { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<PensionProvider, PensionProviderInput>> Projection = pp =>
        new PensionProviderInput { Name = pp.Name, Website = pp.Website, RowVersion = pp.RowVersion };

    public void ApplyTo(PensionProvider provider)
    {
        provider.Name = Name;
        provider.Website = Website;
    }
}
