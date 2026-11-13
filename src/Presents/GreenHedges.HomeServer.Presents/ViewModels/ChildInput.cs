using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.Presents.Models;

namespace GreenHedges.HomeServer.Presents.ViewModels;

public class ChildInput
{
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<Child, ChildInput>> Projection = c => new ChildInput { Name = c.Name, RowVersion = c.RowVersion };

    public void ApplyTo(Child child) => child.Name = Name;
}
