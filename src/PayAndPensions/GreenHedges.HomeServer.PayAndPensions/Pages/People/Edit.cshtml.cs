using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.People;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public PersonInput Person { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.Where(p => p.Id == id).Select(PersonInput.Projection).FirstOrDefaultAsync();
        if (person is null) return NotFound();
        Person = person;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.People.FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null) return NotFound();

        await PersonRules.ValidateAsync(db, id, Person, ModelState);
        if (!ModelState.IsValid) return Page();

        Person.ApplyTo(existing);
        if (!await db.TrySaveEditAsync(existing, Person.RowVersion))
        {
            if (db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this person after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Person.RowVersion = existing.RowVersion;
            ModelState.Remove("Person.RowVersion");
            return Page();
        }

        TempData["Message"] = "Person updated.";
        return RedirectToPage("Index");
    }
}
