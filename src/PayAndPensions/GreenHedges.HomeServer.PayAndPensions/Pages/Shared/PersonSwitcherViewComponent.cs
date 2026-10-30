using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Shared;

/// <summary>
/// The navbar's people switcher, on every page once there are two or more people: each person links to the page you're
/// on, as theirs. A person's list or new-item page (payslips, tax years, employers, pay elements, import) goes to the
/// same page for the other person (payslips in the same tax year); an edit or delete page to its list, as the item
/// is the first person's; any other page (the dashboard, People) to the dashboard for that person.
/// </summary>
public class PersonSwitcherViewComponent(AppDbContext db) : ViewComponent
{
    /// <summary>Set by a page whose person isn't in its route (the dashboard), so the switcher can show them as current.</summary>
    public const string CurrentPersonKey = "CurrentPersonId";

    public record Option(string Name, string Url, bool Current);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var people = await db.People.OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync();
        if (people.Count < 2) return Content(string.Empty);

        var page = RouteData.Values["page"] as string ?? "/Index";
        var current = ViewData[CurrentPersonKey] as int?
            ?? (int.TryParse(RouteData.Values["personId"] as string, out var id) ? id : null);
        var personPage = RouteData.Values.ContainsKey("personId");
        // Edit and delete pages are of one person's item: switch to that section's list.
        var target = !personPage ? "/Index"
            : page.EndsWith("/Index") || page.EndsWith("/Create") ? page
            : page[..page.LastIndexOf('/')] + "/Index";
        var year = target == "/Payslips/Index" ? Request.Query["year"].ToString() : null;

        var options = people.Select(p => new Option(p.Name,
            personPage
                ? Url.Page(target, new { personId = p.Id, year = string.IsNullOrEmpty(year) ? null : year })!
                : Url.Page("/Index", new { person = p.Id })!,
            p.Id == current)).ToList();
        return View(options);
    }
}
