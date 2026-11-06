using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PensionProviders;

/// <summary>The user's pension providers, each with the pensions (whoever's) that are with it.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public record PensionLink(int PersonId, string PersonName, string Name);

    public record Row(int Id, string Name, string? Website, List<PensionLink> Pensions);

    public List<Row> Providers { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Providers = await db.PensionProviders
            .OrderBy(pp => pp.Name)
            .Select(pp => new Row(pp.Id, pp.Name, pp.Website, pp.Pensions
                .OrderBy(pn => pn.Person!.Name).ThenBy(pn => pn.Name)
                .Select(pn => new PensionLink(pn.PersonId, pn.Person!.Name, pn.Name)).ToList()))
            .ToListAsync();
    }
}
