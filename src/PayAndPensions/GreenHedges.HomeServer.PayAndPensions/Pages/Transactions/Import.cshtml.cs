using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.Services.Import;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Transactions;

/// <summary>
/// Imports a pension's transaction history from the provider's CSV exports, several files at once (an export covers
/// up to 12 months): upload them and see the transactions that are new; then import those. Nothing is kept between the
/// two steps but the files themselves, carried in the preview's form, so the import reads exactly what was previewed.
/// Transactions the pension already has are left out (<see cref="TransactionCsv.NewRows"/>), so overlapping or
/// repeated files are fine. Importing also matches the pension's contributions to its payslips and sets their pension
/// received date (<see cref="PensionPayslips"/>), in the same save.
/// </summary>
[RequestSizeLimit(MaxBytes * 2 + 64 * 1024)]
public class ImportModel(AppDbContext db, PensionPayslips pensionPayslips, ILogger<ImportModel> logger) : PensionPageModel(db)
{
    /// <summary>All the files together: a year's export is a few KB. Base64 in the preview's form is a third bigger.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public const int MaxFiles = 20;

    public sealed record FileSummary(string Name, string? Error, int Lines, List<string> Skipped);

    /// <summary>The uploaded files, base64, carried from the preview to the import.</summary>
    [BindProperty]
    public List<string> Files { get; set; } = [];

    [BindProperty]
    public List<string> FileNames { get; set; } = [];

    public List<FileSummary> Summaries { get; private set; } = [];

    /// <summary>The transactions the files would add, oldest first; null before a preview.</summary>
    public List<CsvTransaction>? NewRows { get; private set; }

    /// <summary>Lines in the files that the pension already has.</summary>
    public int AlreadyThere { get; private set; }

    /// <summary>The contributions, new and old, that payslips would be marked received by; null before a preview.</summary>
    public ContributionMatches? Matches { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await LoadPensionAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostPreviewAsync(List<IFormFile> uploads)
    {
        if (!await LoadPensionAsync()) return NotFound();
        uploads = uploads.Where(u => u.Length > 0).ToList();
        if (uploads.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose one or more transaction history files (.csv) to import.");
            return Page();
        }
        if (uploads.Count > MaxFiles)
        {
            ModelState.AddModelError(string.Empty, $"That's too many files: up to {MaxFiles} at once.");
            return Page();
        }
        if (uploads.Sum(u => u.Length) > MaxBytes)
        {
            ModelState.AddModelError(string.Empty, $"Those files are too big: the limit is {MaxBytes / 1024 / 1024} MB together.");
            return Page();
        }

        Files = [];
        FileNames = [];
        foreach (var upload in uploads)
        {
            using var bytes = new MemoryStream();
            await upload.CopyToAsync(bytes);
            Files.Add(Convert.ToBase64String(bytes.ToArray()));
            FileNames.Add(Path.GetFileName(upload.FileName));
        }
        await PlanAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();
        if (!await PlanAsync()) return Page();
        if (NewRows!.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "There's nothing new to import.");
            return Page();
        }

        Db.PensionTransactions.AddRange(NewRows.Select(r => new PensionTransaction
        {
            PensionId = PensionId, TradeDate = r.TradeDate, SettleDate = r.SettleDate, Reference = r.Reference,
            Description = r.Description, Kind = r.Kind, Fund = r.Fund, UnitPrice = r.UnitPrice, Quantity = r.Quantity, Value = r.Value
        }));
        var received = await pensionPayslips.SetReceivedAsync(Matches!);
        await Db.SaveChangesAsync();

        // Counts only: never the account's details or amounts.
        logger.LogInformation("Imported {Added} pension transactions from {Files} files ({AlreadyThere} already there); {Received} payslips received",
            NewRows.Count, Files.Count, AlreadyThere, received);
        TempData["Message"] = $"Imported {NewRows.Count} {(NewRows.Count == 1 ? "transaction" : "transactions")}" +
            (received > 0 ? $", and set the pension received date on {received} {(received == 1 ? "payslip" : "payslips")}." : ".");
        return RedirectToTransactions(NewRows.Max(r => r.TradeDate));
    }

    /// <summary>Payslips' tax months, e.g. "2026-27 months 2 and 3".</summary>
    public static string Months(List<PayslipPension> payslips) => string.Join("; ", payslips
        .GroupBy(p => p.TaxYear)
        .Select(g =>
        {
            var months = g.Select(p => p.TaxMonth.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
            return months.Count == 1 ? $"{TaxYears.Label(g.Key)} month {months[0]}"
                : $"{TaxYears.Label(g.Key)} months {string.Join(", ", months[..^1])} and {months[^1]}";
        }));

    /// <summary>Reads the files and works out the new transactions. False (with the reason) when there's nothing to read.</summary>
    private async Task<bool> PlanAsync()
    {
        if (Files.Count == 0 || Files.Count != FileNames.Count)
        {
            ModelState.AddModelError(string.Empty, "Choose one or more transaction history files (.csv) to import.");
            Files = [];
            return false;
        }

        var read = new List<IReadOnlyList<CsvTransaction>>();
        for (var i = 0; i < Files.Count; i++)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(Files[i]); }
            catch (FormatException) { bytes = []; }
            var result = TransactionCsv.Read(bytes);
            Summaries.Add(new FileSummary(FileNames[i], result.Error, result.Rows.Count, result.Skipped));
            read.Add(result.Rows);
        }

        var existing = await Db.PensionTransactions.Where(tx => tx.PensionId == PensionId)
            .Select(tx => new { tx.TradeDate, tx.Reference, tx.Description, tx.Value }).ToListAsync();
        var keys = existing.Select(e => new TransactionKey(e.TradeDate, e.Reference, e.Description, e.Value)).ToList();
        NewRows = TransactionCsv.NewRows(read, keys);

        // Each transaction in the files once (as many times as one file has it), less those to add.
        AlreadyThere = TransactionCsv.NewRows(read, []).Count - NewRows.Count;
        Matches = await pensionPayslips.PlanAsync(PensionId, NewRows
            .Where(r => r.Kind == TransactionKind.Contribution).Select(r => new ReceivedContribution(r.TradeDate, r.Value)));
        return true;
    }
}
