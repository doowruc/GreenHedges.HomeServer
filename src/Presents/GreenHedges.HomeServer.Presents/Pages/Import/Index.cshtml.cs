using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using GreenHedges.HomeServer.Presents.Services.Import;

namespace GreenHedges.HomeServer.Presents.Pages.Import;

/// <summary>
/// Imports the presents workbook in two steps: upload it, and see what it would add (each year's presents, the
/// children to add, any budget, and the cash worked out against the sheet's own); then import. Nothing is kept between
/// the two but the file itself, carried in the preview's form, so the import reads exactly what was previewed.
/// </summary>
[RequestSizeLimit(MaxBytes + 64 * 1024)]
public class IndexModel(PresentsImporter importer) : PageModel
{
    /// <summary>The real workbook is well under 100 KB; the limit keeps the file within a form field once encoded.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>The uploaded workbook, base64, carried from the preview to the import.</summary>
    [BindProperty]
    public string? Workbook { get; set; }

    [BindProperty]
    public string? FileName { get; set; }

    public ImportPlan? Plan { get; private set; }

    public void OnGet() { }

    /// <summary>Reads the uploaded workbook and shows what it would add.</summary>
    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? upload)
    {
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
        if (Read() is { } contents) Plan = await importer.PlanAsync(contents);
        return Page();
    }

    /// <summary>Imports the previewed workbook.</summary>
    public async Task<IActionResult> OnPostImportAsync()
    {
        if (Read() is not { } contents) return Page();

        Plan = await importer.PlanAsync(contents);
        if (Plan.Presents == 0)
        {
            ModelState.AddModelError(string.Empty, "There's nothing new to import: every year in this spreadsheet is already here, or has no presents that can be read.");
            return Page();
        }

        var (presents, years, children) = await importer.ImportAsync(Plan);
        var alreadyThere = Plan.Years.Count(y => y.AlreadyThere);
        TempData["Message"] = $"Imported {presents} {(presents == 1 ? "present" : "presents")} in {years} {(years == 1 ? "year" : "years")}" +
                              (children > 0 ? $", and added {children} {(children == 1 ? "child" : "children")}" : "") +
                              (alreadyThere > 0 ? $"; {alreadyThere} {(alreadyThere == 1 ? "year was" : "years were")} already here and left as they are" : "") +
                              ".";
        return RedirectToPage("/History/Index");
    }

    /// <summary>Reads <see cref="Workbook"/>; null (with the reason in the model state) if it can't be.</summary>
    private WorkbookContents? Read()
    {
        WorkbookContents contents;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(Workbook ?? string.Empty));
            contents = PresentsWorkbook.Read(stream);
        }
        // Any failure to read it (bad base64, not a zip, not a workbook, a damaged one) means the same to the user.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ModelState.AddModelError(string.Empty, "That isn't a spreadsheet this can read: it needs an Excel workbook (.xlsx).");
            Workbook = null;
            return null;
        }
        foreach (var problem in contents.Problems) ModelState.AddModelError(string.Empty, problem);
        if (contents.Problems.Count == 0) return contents;
        Workbook = null;
        return null;
    }
}
