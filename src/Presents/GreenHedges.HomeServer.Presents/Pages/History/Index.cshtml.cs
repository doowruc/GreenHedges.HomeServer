using Microsoft.AspNetCore.Mvc.RazorPages;
using GreenHedges.HomeServer.Presents.Services;

namespace GreenHedges.HomeServer.Presents.Pages.History;

/// <summary>Every year with a Christmas, latest first: each child's spent and cash, what each came to, and the year's total.</summary>
public class IndexModel(ChristmasSummaries summaries) : PageModel
{
    public List<ChristmasSummary> Years { get; private set; } = [];

    /// <summary>The children, in order, for the columns (the same in every year).</summary>
    public List<string> Children { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Years = await summaries.AllAsync();
        Children = Years.FirstOrDefault()?.Children.Select(c => c.Name).ToList() ?? [];
    }
}
