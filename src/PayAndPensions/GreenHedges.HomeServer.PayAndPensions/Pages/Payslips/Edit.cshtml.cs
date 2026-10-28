using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Payslips;

public class EditModel(AppDbContext db) : PayslipFormModel(db)
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var payslip = await Db.Payslips.Where(s => s.Id == id && s.PersonId == PersonId)
            .Select(s => new PayslipInput
            {
                EmployerId = s.EmployerId, TaxYear = s.TaxYear, TaxMonth = s.TaxMonth, PayDate = s.PayDate,
                PensionReceivedDate = s.PensionReceivedDate, Notes = s.Notes, RowVersion = s.RowVersion,
                Lines = s.Lines.Select(l => new PayslipLineInput { PayElementId = l.PayElementId, Amount = l.Amount }).ToList()
            })
            .FirstOrDefaultAsync();
        if (payslip is null) return NotFound();
        Payslip = payslip;
        await LoadChoicesAsync();
        ArrangeLines();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        // The lines are saved with the payslip, so they're loaded with it.
        var existing = await Db.Payslips.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id && s.PersonId == PersonId);
        if (existing is null) return NotFound();

        await LoadChoicesAsync();
        await ValidateAsync(id);
        if (!ModelState.IsValid)
        {
            ArrangeLines();
            return Page();
        }

        Payslip.ApplyTo(existing);
        ApplyLines(existing, Payslip.Lines);
        // Always saved as changed, so its version is checked (and moves on) even when only its lines changed: a stale
        // form can't overwrite amounts saved from another tab.
        Db.Entry(existing).State = EntityState.Modified;
        if (!await Db.TrySaveEditAsync(existing, Payslip.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this payslip after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Payslip.RowVersion = existing.RowVersion;
            ModelState.Remove("Payslip.RowVersion");
            ArrangeLines();
            return Page();
        }

        TempData["Message"] = $"Updated the payslip for {TaxYears.MonthName(existing.TaxMonth)} {TaxYears.Label(existing.TaxYear)}.";
        return RedirectToYear(existing.TaxYear);
    }
}
