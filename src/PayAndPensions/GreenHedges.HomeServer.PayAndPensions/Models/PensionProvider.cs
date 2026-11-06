using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>
/// A pension company. It belongs to the signed-in user rather than a person, as several people can have pensions with
/// the same provider.
/// </summary>
public class PensionProvider : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The provider's website or sign-in page.</summary>
    [StringLength(200)]
    public string? Website { get; set; }

    public List<Pension> Pensions { get; set; } = [];
}
