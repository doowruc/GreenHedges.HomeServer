using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>A pension's fund (named as its transactions name it) and the ISIN its daily prices are fetched by.</summary>
public class PensionFund : AuditedEntity
{
    public int Id { get; set; }

    public int PensionId { get; set; }
    public Pension? Pension { get; set; }

    [Required, StringLength(200)]
    public string Fund { get; set; } = string.Empty;

    [Required, StringLength(12)]
    public string Isin { get; set; } = string.Empty;

    /// <summary>
    /// The feed gives this fund's prices in pounds, not pence (it gives each fund in its own currency unit, and doesn't
    /// say which with the prices): they're multiplied by 100 when the fund is valued. The stored prices stay as fetched.
    /// </summary>
    public bool PricesInPounds { get; set; }
}
