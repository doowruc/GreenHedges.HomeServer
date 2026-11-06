using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.StatementTypes;

internal static class StatementTypeRules
{
    /// <summary>
    /// Normalises the input and checks it: the name unique among the pension's types, ignoring case
    /// (<paramref name="id"/> is null for a new type), and projections only turned off when no statement has any.
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int pensionId, int? id, StatementTypeInput type, ModelStateDictionary state)
    {
        type.Name = Names.Normalise(type.Name);
        var name = type.Name.ToLower();
        if (await db.StatementTypes.AnyAsync(t => t.PensionId == pensionId && t.Id != id && t.Name.ToLower() == name))
            state.AddModelError("Type.Name", "This pension already has a statement type with this name.");
        if (id is not null && !type.HasProjections && await db.PensionStatements.AnyAsync(st =>
                st.StatementTypeId == id && (st.ProjectedFund != null || st.ProjectedAnnualIncome != null)))
            state.AddModelError("Type.HasProjections", "Its statements have projections: clear them first.");
    }
}
