using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Payslips;

/// <summary>
/// What the add and edit pages share: the person's employers and pay elements for the form, the form's rules, and
/// saving its lines. Pay element ids come back from the form, so they're always checked against the person's own.
/// </summary>
public abstract class PayslipFormModel(AppDbContext db) : PersonPageModel(db)
{
    public record ElementOption(int Id, string Name, PayElementKind Kind, bool Hidden);

    [BindProperty]
    public PayslipInput Payslip { get; set; } = new();

    /// <summary>The person's pay elements, in payslip order: earnings, deductions, then the employer's.</summary>
    public List<ElementOption> Elements { get; private set; } = [];

    public List<SelectListItem> EmployerOptions { get; private set; } = [];

    public List<SelectListItem> TaxYearOptions { get; private set; } = [];

    public static List<SelectListItem> TaxMonthOptions { get; } = Enumerable.Range(1, 12)
        .Select(m => new SelectListItem($"{m} ({TaxYears.MonthName(m)})", m.ToString())).ToList();

    /// <summary>The element for each of <see cref="PayslipInput.Lines"/>, by index, for the form's labels.</summary>
    public ElementOption ElementFor(PayslipLineInput line) => Elements.First(e => e.Id == line.PayElementId);

    /// <summary>Loads the form's choices. Call after <see cref="PersonPageModel.LoadPersonAsync"/>.</summary>
    protected async Task LoadChoicesAsync()
    {
        var elements = await Db.PayElements.Where(el => el.PersonId == PersonId)
            .Select(el => new { el.Id, el.Name, el.Kind, el.SortOrder, el.Hidden }).ToListAsync();
        // Kind is stored as text, so it's ordered here by its place in the enum.
        Elements = elements.OrderBy(el => el.Kind).ThenBy(el => el.SortOrder).ThenBy(el => el.Name)
            .Select(el => new ElementOption(el.Id, el.Name, el.Kind, el.Hidden)).ToList();

        EmployerOptions = await Db.Employers.Where(m => m.PersonId == PersonId).OrderBy(m => m.Name)
            .Select(m => new SelectListItem(m.Name, m.Id.ToString())).ToListAsync();

        // From the year before the person's first payslip (or this one's) to the year after this tax year.
        var payslips = Db.Payslips.Where(s => s.PersonId == PersonId);
        var current = TaxYears.Of(DateOnly.FromDateTime(DateTime.Today));
        var shown = Payslip.TaxYear > 0 ? Payslip.TaxYear : current;
        var from = Math.Min(await payslips.MinAsync(s => (int?)s.TaxYear) ?? current, shown) - 1;
        var to = Math.Max(await payslips.MaxAsync(s => (int?)s.TaxYear) ?? current, Math.Max(current, shown)) + 1;
        TaxYearOptions = Enumerable.Range(from, to - from + 1).Reverse()
            .Select(year => new SelectListItem(TaxYears.Label(year), year.ToString())).ToList();
    }

    /// <summary>
    /// Puts <see cref="PayslipInput.Lines"/> in the form's order, one per element: every element that isn't hidden, and
    /// hidden ones only where they have an amount. Amounts already there are kept.
    /// </summary>
    protected void ArrangeLines()
    {
        var amounts = Payslip.Lines.Where(l => l.Amount is not null)
            .GroupBy(l => l.PayElementId).ToDictionary(g => g.Key, g => g.First().Amount);
        Payslip.Lines = Elements.Where(e => !e.Hidden || amounts.ContainsKey(e.Id))
            .Select(e => new PayslipLineInput { PayElementId = e.Id, Amount = amounts.GetValueOrDefault(e.Id) })
            .ToList();
    }

    /// <summary>The form's rules (<paramref name="id"/> is null for a new payslip). Call after <see cref="LoadChoicesAsync"/>.</summary>
    protected async Task ValidateAsync(int? id)
    {
        if (!EmployerOptions.Any(o => o.Value == Payslip.EmployerId.ToString()))
            ModelState.AddModelError("Payslip.EmployerId", "Choose one of this person's employers.");

        if (Payslip.TaxMonth is >= 1 and <= 12 &&
            await Db.Payslips.AnyAsync(s => s.PersonId == PersonId && s.Id != id && s.TaxYear == Payslip.TaxYear && s.TaxMonth == Payslip.TaxMonth))
            ModelState.AddModelError("Payslip.TaxMonth",
                $"{PersonName} already has a payslip for month {Payslip.TaxMonth} of {TaxYears.Label(Payslip.TaxYear)}.");

        if (Payslip.PayDate < TaxYears.Start(Payslip.TaxYear) || Payslip.PayDate > TaxYears.End(Payslip.TaxYear))
            ModelState.AddModelError("Payslip.PayDate",
                $"The pay date must be in {TaxYears.Label(Payslip.TaxYear)} ({TaxYears.Start(Payslip.TaxYear):d} to {TaxYears.End(Payslip.TaxYear):d}).");

        // Ids from the form: only the person's own elements, each once.
        var known = Elements.Select(e => e.Id).ToHashSet();
        var posted = Payslip.Lines.Select(l => l.PayElementId).ToList();
        if (posted.Any(elementId => !known.Contains(elementId)) || posted.Distinct().Count() != posted.Count)
            ModelState.AddModelError(string.Empty, "The form's pay elements don't match this person's. Reload the page and try again.");
        else if (Payslip.Lines.All(l => l.Amount is null))
            ModelState.AddModelError(string.Empty, "Enter at least one amount.");
    }

    /// <summary>
    /// Makes <paramref name="payslip"/>'s lines match the form: changed amounts updated, new ones added, blanked ones
    /// removed. Call after <see cref="ValidateAsync"/> passes, with the payslip's lines loaded.
    /// </summary>
    protected static void ApplyLines(Payslip payslip, IEnumerable<PayslipLineInput> lines)
    {
        var wanted = lines.Where(l => l.Amount is not null).ToDictionary(l => l.PayElementId, l => l.Amount!.Value);
        foreach (var line in payslip.Lines.ToList())
        {
            if (wanted.Remove(line.PayElementId, out var amount)) line.Amount = amount;
            else payslip.Lines.Remove(line);
        }
        foreach (var (elementId, amount) in wanted)
            payslip.Lines.Add(new PayslipLine { PayElementId = elementId, Amount = amount });
    }

    protected RedirectToPageResult RedirectToYear(int taxYear) => RedirectToPage("Index", new { personId = PersonId, year = taxYear });
}
