using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

/// <summary>
/// Sets a pension on a range of the person's payslips at once, to fill in their history: every payslip from one tax
/// month to another that has a pension line, replacing any pension already set. It says how many first, and asks.
/// </summary>
public class AssignModel(AppDbContext db, CurrentUser currentUser) : PersonPageModel(db)
{
    [BindProperty] public int FromYear { get; set; }
    [BindProperty] public int FromMonth { get; set; } = 1;
    [BindProperty] public int ToYear { get; set; }
    [BindProperty] public int ToMonth { get; set; } = 12;

    /// <summary>Set by the confirmation's button: assign, rather than say how many it would change.</summary>
    [BindProperty] public bool Confirm { get; set; }

    /// <summary>
    /// How many payslips the confirmation said it would change. If that's no longer so (the range was changed after
    /// checking, or payslips were), it says again rather than assigning.
    /// </summary>
    [BindProperty] public int Expected { get; set; }

    public string PensionName { get; private set; } = string.Empty;

    public List<SelectListItem> YearOptions { get; private set; } = [];

    public static List<SelectListItem> MonthOptions => Payslips.PayslipFormModel.TaxMonthOptions;

    /// <summary>After a first submit: the payslips in the range with a pension line, and how many of them go elsewhere now.</summary>
    public int? Matching { get; private set; }
    public int AlreadySet { get; private set; }
    public int Elsewhere { get; private set; }

    public string Range => $"{TaxYears.MonthName(FromMonth)} {TaxYears.Label(FromYear)} to {TaxYears.MonthName(ToMonth)} {TaxYears.Label(ToYear)}";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        // The whole of the person's payslips with pension lines, to start with.
        var months = await WithPension().Select(s => s.TaxYear * 12 + s.TaxMonth - 1).ToListAsync();
        if (months.Count > 0)
        {
            (FromYear, FromMonth) = (months.Min() / 12, months.Min() % 12 + 1);
            (ToYear, ToMonth) = (months.Max() / 12, months.Max() % 12 + 1);
        }
        else if (YearOptions.Count > 0) (FromYear, ToYear) = (int.Parse(YearOptions[^1].Value), int.Parse(YearOptions[0].Value));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (FromMonth is < 1 or > 12 || ToMonth is < 1 or > 12) ModelState.AddModelError(string.Empty, "Choose the tax months.");
        else if (FromYear * 12 + FromMonth > ToYear * 12 + ToMonth)
            ModelState.AddModelError(string.Empty, "The first month must be before the last.");
        if (!ModelState.IsValid) return Page();

        var range = InRange();
        Matching = await range.CountAsync();
        AlreadySet = await range.CountAsync(s => s.PensionId == id);
        Elsewhere = await range.CountAsync(s => s.PensionId != null && s.PensionId != id);
        if (!Confirm || Matching == AlreadySet || Matching - AlreadySet != Expected) return Page();

        var changed = await range.Where(s => s.PensionId == null || s.PensionId != id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.PensionId, id)
            .SetProperty(p => p.ModifiedBy, currentUser.Id!)
            .SetProperty(p => p.ModifiedDate, DateTime.UtcNow));
        TempData["Message"] = $"{PensionName} set on {changed} {(changed == 1 ? "payslip" : "payslips")}, {Range}.";
        return RedirectToIndex();
    }

    private async Task<bool> LoadAsync(int id)
    {
        if (!await LoadPersonAsync()) return false;
        var name = await Db.Pensions.Where(pn => pn.Id == id && pn.PersonId == PersonId).Select(pn => pn.Name).FirstOrDefaultAsync();
        if (name is null) return false;
        PensionName = name;
        YearOptions = (await Db.Payslips.Where(s => s.PersonId == PersonId).Select(s => s.TaxYear).Distinct().ToListAsync())
            .OrderDescending().Select(y => new SelectListItem(TaxYears.Label(y), y.ToString())).ToList();
        return true;
    }

    /// <summary>The person's payslips with a pension line (as <see cref="PayElement.IsPension"/>, which EF can't translate).</summary>
    private IQueryable<Payslip> WithPension() => Db.Payslips.Where(s => s.PersonId == PersonId &&
        s.Lines.Any(l => l.PayElement!.Category == PayCategory.Pension && l.PayElement.Kind != PayElementKind.Earning));

    private IQueryable<Payslip> InRange()
    {
        int from = FromYear * 12 + FromMonth, to = ToYear * 12 + ToMonth;
        return WithPension().Where(s => s.TaxYear * 12 + s.TaxMonth >= from && s.TaxYear * 12 + s.TaxMonth <= to);
    }
}
