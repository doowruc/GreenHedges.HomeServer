using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class EmployerInput
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<Employer, EmployerInput>> Projection = m => new EmployerInput { Name = m.Name, RowVersion = m.RowVersion };

    public void ApplyTo(Employer employer) => employer.Name = Name;
}
