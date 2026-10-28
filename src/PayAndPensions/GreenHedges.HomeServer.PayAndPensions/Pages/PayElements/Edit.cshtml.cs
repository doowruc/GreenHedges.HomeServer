using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PayElements;

public class EditModel(AppDbContext db) : PersonPageModel(db)
{
    [BindProperty]
    public PayElementInput Element { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var element = await Db.PayElements.Where(el => el.Id == id && el.PersonId == PersonId)
            .Select(PayElementInput.Projection).FirstOrDefaultAsync();
        if (element is null) return NotFound();
        Element = element;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var existing = await Db.PayElements.FirstOrDefaultAsync(el => el.Id == id && el.PersonId == PersonId);
        if (existing is null) return NotFound();

        await PayElementRules.ValidateAsync(Db, PersonId, id, Element, ModelState);
        if (!ModelState.IsValid) return Page();

        Element.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Element.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this pay element after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Element.RowVersion = existing.RowVersion;
            ModelState.Remove("Element.RowVersion");
            return Page();
        }

        TempData["Message"] = "Pay element updated.";
        return RedirectToIndex();
    }
}
