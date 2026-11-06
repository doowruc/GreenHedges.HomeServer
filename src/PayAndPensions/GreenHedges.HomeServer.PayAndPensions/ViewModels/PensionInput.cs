using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class PensionInput
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Comes back from the form, so it's checked against the user's own providers.</summary>
    [Display(Name = "Provider"), Range(1, int.MaxValue, ErrorMessage = "Choose the provider.")]
    public int PensionProviderId { get; set; }

    [StringLength(50), Display(Name = "Policy number (optional)")]
    public string? PolicyNumber { get; set; }

    public PensionType Type { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<Pension, PensionInput>> Projection = pn => new PensionInput
    {
        Name = pn.Name, PensionProviderId = pn.PensionProviderId, PolicyNumber = pn.PolicyNumber, Type = pn.Type,
        RowVersion = pn.RowVersion
    };

    public void ApplyTo(Pension pension)
    {
        pension.Name = Name;
        pension.PensionProviderId = PensionProviderId;
        pension.PolicyNumber = PolicyNumber;
        pension.Type = Type;
    }
}
