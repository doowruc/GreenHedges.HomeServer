using Microsoft.AspNetCore.Mvc;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Employers;

public class CreateModel(AppDbContext db) : PersonPageModel(db)
{
    [BindProperty]
    public EmployerInput Employer { get; set; } = new();

    public async Task<IActionResult> OnGetAsync() => await LoadPersonAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        await EmployerRules.ValidateAsync(Db, PersonId, null, Employer, ModelState);
        if (!ModelState.IsValid) return Page();

        var employer = new Employer { PersonId = PersonId };
        Employer.ApplyTo(employer);
        Db.Employers.Add(employer);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added {employer.Name}.";
        return RedirectToIndex();
    }
}
