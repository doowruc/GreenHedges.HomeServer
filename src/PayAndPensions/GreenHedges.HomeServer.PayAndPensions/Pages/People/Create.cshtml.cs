using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.People;

public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public PersonInput Person { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await PersonRules.ValidateAsync(db, null, Person, ModelState);
        if (!ModelState.IsValid) return Page();

        var person = new Person();
        Person.ApplyTo(person);
        db.People.Add(person);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {person.Name}.";
        return RedirectToPage("Index");
    }
}
