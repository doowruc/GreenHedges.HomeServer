using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(int Id, string Name, int Vehicles, string? DriverNumber, DateOnly? LicenceIssueDate, int Claims = 0)
    {
        /// <summary>Whole years since the licence was first issued, as of <paramref name="today"/>.</summary>
        public int? YearsHeld(DateOnly today) => LicenceIssueDate is { } issued
            ? today.Year - issued.Year - (today < issued.AddYears(today.Year - issued.Year) ? 1 : 0)
            : null;
    }

    public List<Row> Owners { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Owners = await db.Owners
            .OrderBy(o => o.Name)
            .Select(o => new Row(o.Id, o.Name, o.Vehicles.Count, o.DriverNumber, o.LicenceIssueDate, o.Claims.Count))
            .ToListAsync();
    }
}
