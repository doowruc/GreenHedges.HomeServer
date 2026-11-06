using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

/// <summary>The add and edit pages' shared form: the pension, and the user's providers to choose from.</summary>
public abstract class PensionFormModel(AppDbContext db) : PersonPageModel(db)
{
    [BindProperty]
    public PensionInput Pension { get; set; } = new();

    public List<SelectListItem> Providers { get; private set; } = [];

    protected async Task LoadProvidersAsync() =>
        Providers = await Db.PensionProviders.OrderBy(pp => pp.Name)
            .Select(pp => new SelectListItem(pp.Name, pp.Id.ToString())).ToListAsync();
}
