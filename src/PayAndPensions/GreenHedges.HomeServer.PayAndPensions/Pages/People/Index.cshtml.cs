using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.People;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(int Id, string Name, int Employers, int PayElements, int Payslips);

    public List<Row> People { get; private set; } = [];

    public async Task OnGetAsync()
    {
        People = await db.People
            .OrderBy(p => p.Name)
            .Select(p => new Row(p.Id, p.Name, p.Employers.Count, p.PayElements.Count, p.Payslips.Count))
            .ToListAsync();
    }
}
