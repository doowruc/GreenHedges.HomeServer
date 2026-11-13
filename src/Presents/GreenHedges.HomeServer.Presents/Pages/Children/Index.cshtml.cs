using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;

namespace GreenHedges.HomeServer.Presents.Pages.Children;

/// <summary>The children, in their order, with buttons to move one up or down.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(int Id, string Name, int Purchases);

    public List<Row> Children { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Children = await db.Children
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new Row(c.Id, c.Name, c.Purchases.Count))
            .ToListAsync();
    }

    public Task<IActionResult> OnPostUpAsync(int id) => MoveAsync(id, -1);

    public Task<IActionResult> OnPostDownAsync(int id) => MoveAsync(id, +1);

    /// <summary>
    /// Swaps the child with the one before or after it. Renumbers them all 1, 2, 3… as it goes, so gaps or ties left
    /// by deletes (or an import) never stop a move.
    /// </summary>
    private async Task<IActionResult> MoveAsync(int id, int by)
    {
        var children = await db.Children.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync();
        var from = children.FindIndex(c => c.Id == id);
        if (from < 0) return NotFound();
        var to = from + by;
        if (to >= 0 && to < children.Count) (children[from], children[to]) = (children[to], children[from]);

        for (var i = 0; i < children.Count; i++) children[i].DisplayOrder = i + 1;
        await db.SaveChangesAsync();
        return RedirectToPage();
    }
}
