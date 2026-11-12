using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.Utilities.Models;

/// <summary>What a meter showed on a date: one per meter and date. Units used, days and units a day are worked out.</summary>
public class Reading : AuditedEntity
{
    public int Id { get; set; }

    public int MeterId { get; set; }
    public Meter? Meter { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>In the meter's unit, to three decimal places (a smart gas meter's thousandths of a cubic metre).</summary>
    public decimal Value { get; set; }
}
