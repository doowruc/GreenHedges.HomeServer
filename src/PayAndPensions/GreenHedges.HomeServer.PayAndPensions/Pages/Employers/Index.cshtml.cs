using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Employers;

public class IndexModel(AppDbContext db) : PersonPageModel(db)
{
    public record Row(int Id, string Name, int Payslips);

    public List<Row> Employers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        Employers = await Db.Employers.Where(m => m.PersonId == PersonId)
            .OrderBy(m => m.Name)
            .Select(m => new Row(m.Id, m.Name, m.Payslips.Count))
            .ToListAsync();
        return Page();
    }
}
