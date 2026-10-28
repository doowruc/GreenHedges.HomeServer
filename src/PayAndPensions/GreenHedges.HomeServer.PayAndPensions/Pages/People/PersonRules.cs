using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.People;

internal static class PersonRules
{
    /// <summary>
    /// Normalises the input and checks the name is unique, ignoring case
    /// (<paramref name="id"/> is null for a new person).
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int? id, PersonInput person, ModelStateDictionary state)
    {
        person.Name = Names.Normalise(person.Name);
        var name = person.Name.ToLower();
        if (await db.People.AnyAsync(p => p.Id != id && p.Name.ToLower() == name))
            state.AddModelError("Person.Name", "A person with this name already exists.");
    }
}
