using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>
/// Someone whose pay (and later pensions) is kept, e.g. me or my partner. Everything else belongs to a person. Pages
/// use <c>PersonInput</c> or their own projections rather than this entity.
/// </summary>
public class Person : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    public List<Employer> Employers { get; set; } = [];
    public List<PayElement> PayElements { get; set; } = [];
    public List<Payslip> Payslips { get; set; } = [];
}
