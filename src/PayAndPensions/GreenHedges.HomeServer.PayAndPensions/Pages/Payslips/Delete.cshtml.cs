using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Payslips;

/// <summary>Deletes a payslip with its lines.</summary>
public class DeleteModel(AppDbContext db) : PersonPageModel(db)
{
    public int TaxYear { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var payslip = await Db.Payslips.Where(s => s.Id == id && s.PersonId == PersonId)
            .Select(s => new { s.TaxYear, s.TaxMonth, s.PayDate, Employer = s.Employer!.Name }).FirstOrDefaultAsync();
        if (payslip is null) return NotFound();
        TaxYear = payslip.TaxYear;
        Description = $"{TaxYears.MonthName(payslip.TaxMonth)} {TaxYears.Label(payslip.TaxYear)} (month {payslip.TaxMonth}, paid {payslip.PayDate:d} by {payslip.Employer})";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var payslip = Db.Payslips.Where(s => s.Id == id && s.PersonId == PersonId);
        var found = await payslip.Select(s => new { s.TaxYear, s.TaxMonth }).FirstOrDefaultAsync();
        if (found is null) return RedirectToPage("Index", new { personId = PersonId });

        await payslip.ExecuteDeleteAsync();   // its lines go with it (FK cascade)
        TempData["Message"] = $"Deleted the payslip for {TaxYears.MonthName(found.TaxMonth)} {TaxYears.Label(found.TaxYear)}.";
        return RedirectToPage("Index", new { personId = PersonId, year = found.TaxYear });
    }
}
