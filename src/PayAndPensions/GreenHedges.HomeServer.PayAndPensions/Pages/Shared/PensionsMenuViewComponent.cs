using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Shared;

/// <summary>The navbar's Pensions menu: the providers (shared), then each person's pensions.</summary>
public class PensionsMenuViewComponent(AppDbContext db) : ViewComponent
{
    public record Person(int Id, string Name);

    public async Task<IViewComponentResult> InvokeAsync() =>
        View(await db.People.OrderBy(p => p.Name).Select(p => new Person(p.Id, p.Name)).ToListAsync());
}
