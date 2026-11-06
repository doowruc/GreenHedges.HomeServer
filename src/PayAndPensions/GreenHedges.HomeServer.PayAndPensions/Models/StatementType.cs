using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>
/// A kind of statement a pension's provider sends, e.g. a quarterly investment report or an annual illustration. One
/// with projections also gives the projected fund at retirement and annual income.
/// </summary>
public class StatementType : AuditedEntity
{
    public int Id { get; set; }

    public int PensionId { get; set; }
    public Pension? Pension { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool HasProjections { get; set; }

    public List<PensionStatement> Statements { get; set; } = [];
}
