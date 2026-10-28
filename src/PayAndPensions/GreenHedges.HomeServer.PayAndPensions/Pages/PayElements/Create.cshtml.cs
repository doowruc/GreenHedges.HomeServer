using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PayElements;

public class CreateModel(AppDbContext db) : PersonPageModel(db)
{
    [BindProperty]
    public PayElementInput Element { get; set; } = new();

    /// <summary>A new element goes after the person's others, unless its order is changed.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        var last = await Db.PayElements.Where(el => el.PersonId == PersonId).MaxAsync(el => (int?)el.SortOrder);
        Element.SortOrder = last is null ? 10 : last.Value + 10;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        await PayElementRules.ValidateAsync(Db, PersonId, null, Element, ModelState);
        if (!ModelState.IsValid) return Page();

        var element = new PayElement { PersonId = PersonId };
        Element.ApplyTo(element);
        Db.PayElements.Add(element);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added {element.Name}.";
        return RedirectToIndex();
    }
}
