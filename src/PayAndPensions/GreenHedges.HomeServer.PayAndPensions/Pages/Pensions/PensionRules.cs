using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

internal static class PensionRules
{
    /// <summary>
    /// Normalises the input and checks it: the name unique among the person's pensions, ignoring case
    /// (<paramref name="id"/> is null for a new pension), the provider one of the user's, and the type a real one.
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int personId, int? id, PensionInput pension, ModelStateDictionary state)
    {
        pension.Name = Names.Normalise(pension.Name);
        pension.PolicyNumber = Names.Normalise(pension.PolicyNumber) is { Length: > 0 } policy ? policy : null;
        var name = pension.Name.ToLower();
        if (await db.Pensions.AnyAsync(pn => pn.PersonId == personId && pn.Id != id && pn.Name.ToLower() == name))
            state.AddModelError("Pension.Name", "This person already has a pension with this name.");
        if (pension.PensionProviderId > 0 && !await db.PensionProviders.AnyAsync(pp => pp.Id == pension.PensionProviderId))
            state.AddModelError("Pension.PensionProviderId", "Choose the provider.");
        if (!Enum.IsDefined(pension.Type))
            state.AddModelError("Pension.Type", "Choose the type.");
    }
}
