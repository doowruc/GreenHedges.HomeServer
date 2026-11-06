using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Statements;

/// <summary>
/// What the add and edit pages share: the pension's statement types for the form, and the form's rules. The type id
/// comes back from the form, so it's checked against the pension's own.
/// </summary>
public abstract class StatementFormModel(AppDbContext db) : PensionPageModel(db)
{
    public record TypeOption(int Id, string Name, bool HasProjections);

    [BindProperty]
    public StatementInput Statement { get; set; } = new();

    public List<TypeOption> Types { get; private set; } = [];

    public List<SelectListItem> TypeOptions => Types.Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToList();

    /// <summary>Loads the form's choices. Call after <see cref="PensionPageModel.LoadPensionAsync"/>.</summary>
    protected async Task LoadTypesAsync() =>
        Types = await Db.StatementTypes.Where(t => t.PensionId == PensionId).OrderBy(t => t.Name)
            .Select(t => new TypeOption(t.Id, t.Name, t.HasProjections)).ToListAsync();

    /// <summary>The form's rules (<paramref name="id"/> is null for a new statement). Call after <see cref="LoadTypesAsync"/>.</summary>
    protected async Task ValidateAsync(int? id)
    {
        Statement.Label = Names.Normalise(Statement.Label) is { Length: > 0 } label ? label : null;
        var type = Types.FirstOrDefault(t => t.Id == Statement.StatementTypeId);
        if (type is null)
        {
            ModelState.AddModelError("Statement.StatementTypeId", "Choose one of this pension's statement types.");
            return;
        }
        if (!type.HasProjections)
        {
            if (Statement.ProjectedFund is not null)
                ModelState.AddModelError("Statement.ProjectedFund", $"{type.Name} statements have no projections.");
            if (Statement.ProjectedAnnualIncome is not null)
                ModelState.AddModelError("Statement.ProjectedAnnualIncome", $"{type.Name} statements have no projections.");
        }
        if (await Db.PensionStatements.AnyAsync(st => st.StatementTypeId == type.Id && st.Id != id && st.Date == Statement.Date))
            ModelState.AddModelError("Statement.Date", $"There's already a {type.Name} statement on {Statement.Date:d}.");
    }
}
