using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.Services.Import;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Import;

/// <summary>
/// Imports a person's payslip spreadsheet in two steps: upload it, and see what it would become (each column's pay
/// element, the rows, and a check against the sheet's own totals); then import. Nothing is kept between the two but the
/// file itself, carried in the preview's form, so the import reads exactly what was previewed.
/// </summary>
[RequestSizeLimit(MaxBytes + 64 * 1024)]
public class IndexModel(AppDbContext db, PayslipImporter importer) : PersonPageModel(db)
{
    /// <summary>Real workbooks are about 50 KB; the limit keeps the file within a form field once encoded.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public class ColumnInput
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public PayElementKind Kind { get; set; }
        public PayCategory Category { get; set; }
    }

    /// <summary>The uploaded workbook, base64, carried from the preview to the import.</summary>
    [BindProperty]
    public string? Workbook { get; set; }

    [BindProperty]
    public string? FileName { get; set; }

    [BindProperty]
    public List<ColumnInput> Columns { get; set; } = [];

    /// <summary>One of the person's employers, or 0 for a new one called <see cref="NewEmployerName"/>.</summary>
    [BindProperty]
    public int EmployerId { get; set; }

    [BindProperty]
    public string? NewEmployerName { get; set; }

    public WorkbookContents? Contents { get; private set; }

    public ImportPlan? Plan { get; private set; }

    public List<SelectListItem> EmployerOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await LoadPersonAsync() ? Page() : NotFound();

    /// <summary>Reads the uploaded workbook and shows what it would become.</summary>
    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? upload)
    {
        if (!await LoadPersonAsync()) return NotFound();
        if (upload is null || upload.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose a spreadsheet (.xlsx) to import.");
            return Page();
        }
        if (upload.Length > MaxBytes)
        {
            ModelState.AddModelError(string.Empty, $"That file is too big: the limit is {MaxBytes / 1024 / 1024} MB.");
            return Page();
        }

        using var bytes = new MemoryStream();
        await upload.CopyToAsync(bytes);
        Workbook = Convert.ToBase64String(bytes.ToArray());
        FileName = Path.GetFileName(upload.FileName);
        if (!Read()) return Page();

        Plan = await importer.PlanAsync(PersonId, Contents!);
        Columns = Plan.Columns.Select(c => new ColumnInput
        {
            Index = c.Column.Index, Name = c.Choice.Name, Kind = c.Choice.Kind, Category = c.Choice.Category
        }).ToList();
        await LoadEmployersAsync();
        EmployerId = EmployerOptions.Count > 1 ? int.Parse(EmployerOptions[1].Value) : 0;
        return Page();
    }

    /// <summary>Imports the previewed workbook with the columns as confirmed.</summary>
    public async Task<IActionResult> OnPostImportAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        if (!Read()) return Page();

        var choices = Columns.Select(c => new ColumnChoice(c.Index, c.Name ?? string.Empty, c.Kind, c.Category)).ToList();
        Plan = await importer.PlanAsync(PersonId, Contents!, choices);
        await LoadEmployersAsync();
        var newEmployer = EmployerId == 0 ? Names.Normalise(NewEmployerName) : null;
        if (EmployerId == 0 && newEmployer is not { Length: > 0 and <= 100 })
            ModelState.AddModelError(nameof(NewEmployerName), "Name the new employer (up to 100 characters).");
        else if (EmployerId != 0 && !EmployerOptions.Any(o => o.Value == EmployerId.ToString()))
            ModelState.AddModelError(nameof(EmployerId), "Choose one of this person's employers.");
        else if (newEmployer is not null &&
                 await Db.Employers.AnyAsync(m => m.PersonId == PersonId && m.Name.ToLower() == newEmployer.ToLower()))
            ModelState.AddModelError(nameof(NewEmployerName), $"{PersonName} already has an employer called {newEmployer}: choose it instead.");
        foreach (var problem in Plan.Problems) ModelState.AddModelError(string.Empty, problem);
        if (Plan.ToImport.Count == 0 && Plan.Problems.Count == 0)
            ModelState.AddModelError(string.Empty, $"{PersonName} already has a payslip for every month in this workbook.");
        if (!ModelState.IsValid) return Page();

        var result = await importer.ImportAsync(PersonId, Plan, EmployerId == 0 ? null : EmployerId, newEmployer);
        TempData["Message"] = $"Imported {result.Payslips} payslips" +
                              (result.NewPayElements > 0 ? $" and {result.NewPayElements} new pay elements" : "") +
                              (Plan.AlreadyThere.Count > 0 ? $"; {Plan.AlreadyThere.Count} months were already there and were left as they are" : "") +
                              ".";
        return RedirectToPage("/Payslips/Index", new { personId = PersonId, year = Plan.ToImport.Max(r => r.TaxYear) });
    }

    /// <summary>Reads <see cref="Workbook"/>; false (with the reason in the model state) if it can't be.</summary>
    private bool Read()
    {
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(Workbook ?? string.Empty));
            Contents = PayslipWorkbook.Read(stream);
        }
        // Any failure to read it (bad base64, not a zip, not a workbook, a damaged one) means the same to the user.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not an .xlsx (or not a whole one): say so, without its details.
            ModelState.AddModelError(string.Empty, "That isn't a spreadsheet this can read: it needs an Excel workbook (.xlsx).");
            Workbook = null;
            return false;
        }
        foreach (var problem in Contents.Problems) ModelState.AddModelError(string.Empty, problem);
        if (Contents.Problems.Count > 0) Workbook = null;
        return Contents.Problems.Count == 0;
    }

    private async Task LoadEmployersAsync()
    {
        EmployerOptions = [new SelectListItem("A new employer…", "0")];
        EmployerOptions.AddRange(await Db.Employers.Where(m => m.PersonId == PersonId).OrderBy(m => m.Name)
            .Select(m => new SelectListItem(m.Name, m.Id.ToString())).ToListAsync());
    }

    public static string Months(IEnumerable<WorkbookRow> rows) =>
        rows.Any() ? $"{TaxYears.MonthName(rows.First().TaxMonth)} {TaxYears.Label(rows.First().TaxYear)} to {TaxYears.MonthName(rows.Last().TaxMonth)} {TaxYears.Label(rows.Last().TaxYear)}" : "";
}
