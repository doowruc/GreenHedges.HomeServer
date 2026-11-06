using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Statements;

/// <summary>A pension's statements: a table per statement type, latest first, with the figures worked out from them.</summary>
public class IndexModel(AppDbContext db) : PensionPageModel(db)
{
    public record Row(int Id, DateOnly Date, string? Label, decimal Value, decimal? ProjectedFund, decimal? ProjectedAnnualIncome,
        string? Notes, StatementFigures Figures);

    public record TypeTable(int Id, string Name, bool HasProjections, List<Row> Rows);

    public List<TypeTable> Types { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();
        var types = await Db.StatementTypes.Where(t => t.PensionId == PensionId).OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id, t.Name, t.HasProjections,
                Statements = t.Statements.OrderBy(st => st.Date)
                    .Select(st => new { st.Id, st.Date, st.Label, st.Value, st.ProjectedFund, st.ProjectedAnnualIncome, st.Notes })
                    .ToList()
            })
            .ToListAsync();

        Types = types.Select(t =>
        {
            var figures = StatementCalculations.Calculate(StartDate, t.Statements.Select(st => new StatementValue(st.Date, st.Value)).ToList());
            var rows = t.Statements.Select((st, i) => new Row(st.Id, st.Date, st.Label, st.Value, st.ProjectedFund,
                st.ProjectedAnnualIncome, st.Notes, figures[i])).Reverse().ToList();
            return new TypeTable(t.Id, t.Name, t.HasProjections, rows);
        }).ToList();
        return Page();
    }
}
