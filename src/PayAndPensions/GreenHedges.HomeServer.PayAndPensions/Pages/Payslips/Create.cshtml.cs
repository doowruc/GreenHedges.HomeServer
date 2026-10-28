using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Payslips;

public class CreateModel(AppDbContext db) : PayslipFormModel(db)
{
    /// <summary>
    /// A new payslip starts as the month after the person's latest: same employer and amounts, the next tax month, and a
    /// pay date a month on. Usually only a few figures change. Their first starts from today.
    /// </summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();

        var latest = await Db.Payslips.Where(s => s.PersonId == PersonId)
            .OrderByDescending(s => s.TaxYear).ThenByDescending(s => s.TaxMonth)
            .Select(s => new
            {
                s.EmployerId, s.TaxYear, s.TaxMonth, s.PayDate,
                Lines = s.Lines.Select(l => new PayslipLineInput { PayElementId = l.PayElementId, Amount = l.Amount }).ToList()
            })
            .FirstOrDefaultAsync();
        if (latest is null)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            Payslip = new PayslipInput { TaxYear = TaxYears.Of(today), TaxMonth = TaxYears.MonthOf(today), PayDate = today };
        }
        else
        {
            var (taxYear, taxMonth) = TaxYears.Next(latest.TaxYear, latest.TaxMonth);
            Payslip = new PayslipInput
            {
                EmployerId = latest.EmployerId, TaxYear = taxYear, TaxMonth = taxMonth, PayDate = latest.PayDate.AddMonths(1),
                Lines = latest.Lines
            };
        }

        await LoadChoicesAsync();
        if (latest is null && EmployerOptions.Count > 0) Payslip.EmployerId = int.Parse(EmployerOptions[0].Value);
        // Hidden elements are left off new payslips, even if the last one had them.
        var hidden = Elements.Where(e => e.Hidden).Select(e => e.Id).ToHashSet();
        Payslip.Lines.RemoveAll(l => hidden.Contains(l.PayElementId));
        ArrangeLines();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        await LoadChoicesAsync();
        await ValidateAsync(null);
        if (!ModelState.IsValid)
        {
            ArrangeLines();
            return Page();
        }

        var payslip = new Payslip { PersonId = PersonId };
        Payslip.ApplyTo(payslip);
        ApplyLines(payslip, Payslip.Lines);
        Db.Payslips.Add(payslip);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added the payslip for {TaxYears.MonthName(payslip.TaxMonth)} {TaxYears.Label(payslip.TaxYear)}.";
        return RedirectToYear(payslip.TaxYear);
    }
}
