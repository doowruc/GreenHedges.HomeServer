using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Employers;

internal static class EmployerRules
{
    /// <summary>
    /// Normalises the input and checks the name is unique among the person's employers, ignoring case
    /// (<paramref name="id"/> is null for a new employer).
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int personId, int? id, EmployerInput employer, ModelStateDictionary state)
    {
        employer.Name = Names.Normalise(employer.Name);
        var name = employer.Name.ToLower();
        if (await db.Employers.AnyAsync(m => m.PersonId == personId && m.Id != id && m.Name.ToLower() == name))
            state.AddModelError("Employer.Name", "This person already has an employer with this name.");
    }
}
