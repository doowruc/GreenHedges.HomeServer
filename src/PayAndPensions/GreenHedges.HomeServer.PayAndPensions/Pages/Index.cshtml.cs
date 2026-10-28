using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages;

/// <summary>The dashboard. For now, each person and what's set up for them, with links to their payslips.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public record PersonSummary(int Id, string Name, int Employers, int PayElements, int Payslips);

    public List<PersonSummary> People { get; private set; } = [];

    public async Task OnGetAsync()
    {
        People = await db.People
            .OrderBy(p => p.Name)
            .Select(p => new PersonSummary(p.Id, p.Name, p.Employers.Count, p.PayElements.Count, p.Payslips.Count))
            .ToListAsync();
    }
}
