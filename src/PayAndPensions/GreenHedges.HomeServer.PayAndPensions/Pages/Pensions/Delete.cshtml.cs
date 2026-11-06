using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

public class DeleteModel(AppDbContext db) : PersonPageModel(db)
{
    public string PensionName { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var name = await Db.Pensions.Where(pn => pn.Id == id && pn.PersonId == PersonId).Select(pn => pn.Name).FirstOrDefaultAsync();
        if (name is null) return NotFound();
        PensionName = name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var pension = Db.Pensions.Where(pn => pn.Id == id && pn.PersonId == PersonId);
        var name = await pension.Select(pn => pn.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToIndex();

        await pension.ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToIndex();
    }
}
