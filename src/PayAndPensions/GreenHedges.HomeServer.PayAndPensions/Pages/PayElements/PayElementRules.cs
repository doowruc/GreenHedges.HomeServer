using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PayElements;

internal static class PayElementRules
{
    /// <summary>
    /// Normalises the input and checks the name is unique among the person's pay elements, ignoring case
    /// (<paramref name="id"/> is null for a new one), and that only a pension deduction is salary sacrifice.
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int personId, int? id, PayElementInput element, ModelStateDictionary state)
    {
        element.Name = Names.Normalise(element.Name);
        var name = element.Name.ToLower();
        if (await db.PayElements.AnyAsync(el => el.PersonId == personId && el.Id != id && el.Name.ToLower() == name))
            state.AddModelError("Element.Name", "This person already has a pay element with this name.");
        if (element.SalarySacrifice && (element.Kind != PayElementKind.Deduction || element.Category != PayCategory.Pension))
            state.AddModelError("Element.SalarySacrifice", "Only a pension deduction can be salary sacrifice.");
    }
}
