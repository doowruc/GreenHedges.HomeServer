using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>Someone a person is (or was) paid by. Every payslip is from one.</summary>
public class Employer : AuditedEntity
{
    public int Id { get; set; }

    public int PersonId { get; set; }
    public Person? Person { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public List<Payslip> Payslips { get; set; } = [];
}
