using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;

namespace GreenHedges.HomeServer.Presents.Pages;

/// <summary>This Christmas: each child and how many presents they have so far.</summary>
public class IndexModel(AppDbContext db, TimeProvider time) : PageModel
{
    public record ChildRow(int Id, string Name, int Presents);

    public int Year { get; private set; }

    public List<ChildRow> Children { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Year = time.GetLocalNow().Year;
        var year = Year;
        Children = await db.Children
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new ChildRow(c.Id, c.Name, c.Purchases.Where(p => p.Christmas!.Year == year).Sum(p => (int?)p.Quantity) ?? 0))
            .ToListAsync();
    }
}
