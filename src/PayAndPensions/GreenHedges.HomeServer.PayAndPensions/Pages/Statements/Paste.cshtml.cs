using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.Services.Import;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Statements;

/// <summary>
/// Brings in a statement type's history, pasted from a spreadsheet (<see cref="StatementPaste"/>): paste, preview,
/// import. Nothing is kept between the steps but the pasted text, so the import reads exactly what was previewed. Dates
/// the type already has are left alone; a first row with a value of 0 can become the pension's start date.
/// </summary>
public class PasteModel(AppDbContext db, ILogger<PasteModel> logger) : PensionPageModel(db)
{
    public record PreviewRow(PastedStatement Statement, StatementFigures Figures);

    [BindProperty(SupportsGet = true)]
    public int TypeId { get; set; }

    [BindProperty, StringLength(500_000, ErrorMessage = "That's too much to paste at once.")]
    public string? Text { get; set; }

    /// <summary>Set the pension's start date from the paste's 0 first row (offered when it has none).</summary>
    [BindProperty]
    public bool SetStartDate { get; set; } = true;

    public string TypeName { get; private set; } = string.Empty;

    public bool HasProjections { get; private set; }

    public StatementPasteResult? Result { get; private set; }

    /// <summary>The rows to import, oldest first, with their figures among the type's statements.</summary>
    public List<PreviewRow> New { get; private set; } = [];

    /// <summary>Rows whose dates the type already has: left as they are.</summary>
    public List<PastedStatement> Existing { get; private set; } = [];

    /// <summary>The start date the paste offers, if the pension has none yet.</summary>
    public DateOnly? OfferedStartDate => StartDate is null ? Result?.StartDate : null;

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (ModelState.IsValid) await PlanAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (!ModelState.IsValid) return Page();
        var existing = await PlanAsync();
        if (New.Count == 0) return Page();

        Db.PensionStatements.AddRange(New.Select(r => new PensionStatement
        {
            StatementTypeId = TypeId, Date = r.Statement.Date, Label = r.Statement.Label, Value = r.Statement.Value,
            ProjectedFund = r.Statement.ProjectedFund, ProjectedAnnualIncome = r.Statement.ProjectedAnnualIncome
        }));
        var started = SetStartDate && OfferedStartDate is not null;
        if (started)
        {
            var pension = await Db.Pensions.FirstAsync(pn => pn.Id == PensionId);
            pension.StartDate = OfferedStartDate;
        }
        await Db.SaveChangesAsync();
        logger.LogInformation("Pasted {Count} statements in, {Existing} already there", New.Count, existing);
        TempData["Message"] = $"Imported {New.Count} {TypeName} {(New.Count == 1 ? "statement" : "statements")}" +
            (started ? $", and the pension's start date." : ".");
        return RedirectToStatements();
    }

    private async Task<bool> LoadAsync()
    {
        if (!await LoadPensionAsync()) return false;
        var type = await Db.StatementTypes.Where(t => t.Id == TypeId && t.PensionId == PensionId)
            .Select(t => new { t.Name, t.HasProjections }).FirstOrDefaultAsync();
        if (type is null) return false;
        (TypeName, HasProjections) = (type.Name, type.HasProjections);
        return true;
    }

    /// <summary>Reads the paste and splits its rows into new and existing; returns how many exist.</summary>
    private async Task<int> PlanAsync()
    {
        Result = StatementPaste.Parse(Text, HasProjections);
        if (Result.Error is not null) ModelState.AddModelError(nameof(Text), Result.Error);

        var saved = await Db.PensionStatements.Where(st => st.StatementTypeId == TypeId)
            .Select(st => new StatementValue(st.Date, st.Value)).ToListAsync();
        var dates = saved.Select(s => s.Date).ToHashSet();
        Existing = Result.Rows.Where(r => dates.Contains(r.Date)).ToList();
        var added = Result.Rows.Where(r => !dates.Contains(r.Date)).ToList();

        // The figures as they'll be once imported, among the type's statements, from the start date it'll have.
        var all = saved.Concat(added.Select(r => new StatementValue(r.Date, r.Value))).OrderBy(s => s.Date).ToList();
        var start = StartDate ?? (SetStartDate ? Result.StartDate : null);
        var figures = StatementCalculations.Calculate(start, all);
        var byDate = all.Select((s, i) => (s.Date, Figures: figures[i])).ToDictionary(x => x.Date, x => x.Figures);
        New = added.Select(r => new PreviewRow(r, byDate[r.Date])).ToList();
        return Existing.Count;
    }
}
