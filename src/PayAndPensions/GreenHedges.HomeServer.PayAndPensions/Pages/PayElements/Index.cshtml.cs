using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PayElements;

public class IndexModel(AppDbContext db) : PersonPageModel(db)
{
    public record Row(int Id, string Name, PayElementKind Kind, PayCategory Category, int SortOrder, bool Hidden, int Payslips);

    /// <summary>In the order payslips show them: earnings, deductions, then employer contributions.</summary>
    public List<Row> Elements { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        var elements = await Db.PayElements.Where(el => el.PersonId == PersonId)
            .Select(el => new Row(el.Id, el.Name, el.Kind, el.Category, el.SortOrder, el.Hidden, el.Lines.Count))
            .ToListAsync();
        // Kind is stored as text, so it's ordered here by its place in the enum rather than alphabetically.
        Elements = elements.OrderBy(el => el.Kind).ThenBy(el => el.SortOrder).ThenBy(el => el.Name).ToList();
        return Page();
    }
}
