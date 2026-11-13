using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.Presents.Models;

/// <summary>
/// One of the children whose presents are kept: a user-owned root. Purchases belong to a child and a Christmas. Pages
/// use <c>ChildInput</c> or their own projections rather than this entity.
/// </summary>
public class Child : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Unique per user, ignoring case.</summary>
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Where they come in lists and summaries, lowest first.</summary>
    public int DisplayOrder { get; set; }

    public List<Purchase> Purchases { get; set; } = [];
}
