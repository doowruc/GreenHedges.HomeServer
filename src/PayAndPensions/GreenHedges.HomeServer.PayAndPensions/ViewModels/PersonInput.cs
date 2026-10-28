using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.ViewModels;

public class PersonInput
{
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<Person, PersonInput>> Projection = p => new PersonInput { Name = p.Name, RowVersion = p.RowVersion };

    public void ApplyTo(Person person) => person.Name = Name;
}
