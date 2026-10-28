using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages;

/// <summary>
/// Base for a person's pages, routed under <c>/People/{personId}/…</c> (employers, pay elements). Links and redirects
/// between them must pass <c>personId</c> (ASP.NET doesn't carry it over to a different page, and the link comes out
/// empty without it).
/// </summary>
public abstract class PersonPageModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int PersonId { get; set; }

    public string PersonName { get; private set; } = string.Empty;

    protected AppDbContext Db => db;

    /// <summary>Loads the person from the URL; false if they aren't one of the user's.</summary>
    protected async Task<bool> LoadPersonAsync()
    {
        var name = await db.People.Where(p => p.Id == PersonId).Select(p => p.Name).FirstOrDefaultAsync();
        if (name is null) return false;
        PersonName = name;
        return true;
    }

    protected RedirectToPageResult RedirectToIndex() => RedirectToPage("Index", new { personId = PersonId });
}
