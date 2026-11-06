using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>One statement: a pension's value on a date, and (for a type with projections) what it's projected to be.</summary>
public class PensionStatement : AuditedEntity
{
    public int Id { get; set; }

    public int StatementTypeId { get; set; }
    public StatementType? StatementType { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>What the statement calls itself, e.g. "Spring 2026".</summary>
    [StringLength(50)]
    public string? Label { get; set; }

    public decimal Value { get; set; }

    /// <summary>The projected fund at retirement: only for a type with projections.</summary>
    public decimal? ProjectedFund { get; set; }

    /// <summary>The projected annual income in retirement: only for a type with projections.</summary>
    public decimal? ProjectedAnnualIncome { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
