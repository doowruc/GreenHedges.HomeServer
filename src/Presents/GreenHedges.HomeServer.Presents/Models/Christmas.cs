using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.Presents.Models;

/// <summary>
/// A year's Christmas: a user-owned root, one per user and year. Its purchases are deleted with it. Spent, cash and
/// totals are worked out from the purchases, never stored.
/// </summary>
public class Christmas : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public int Year { get; set; }

    /// <summary>
    /// What each child's Christmas should come to, if there's a budget: cash tops every child up to it (or to the most
    /// any child spent, if that's more). Null: cash only balances the children.
    /// </summary>
    public decimal? Budget { get; set; }

    public List<Purchase> Purchases { get; set; } = [];
}
