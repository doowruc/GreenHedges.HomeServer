using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>
/// The payslip pages. The test person's form lines are in payslip order: Salary (0), Tax (1), NI (2), Pension (3),
/// Employer pension (4).
/// </summary>
public class PayslipPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static string Line(int index) => $"Payslip.Lines[{index}].Amount";

    private async Task<Person> PersonAsync(string name = "Alex")
    {
        await using var db = Db();
        return await TestData.AddPersonAsync(db, name);
    }

    [DatabaseFact]
    public async Task The_first_payslip_starts_from_today_and_saves_only_the_amounts_given()
    {
        var person = await PersonAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";
        var form = await client.GetAsync(url);
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Contains($"value=\"{today:yyyy-MM-dd}\"", form.Html);

        var saved = await client.PostFormAsync(url, form, new() { [Line(0)] = "3000", [Line(1)] = "512.40" });

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        Assert.Equal($"/People/{person.Id}/Payslips?year={TaxYears.Of(today)}", saved.Location);
        await using var check = Db();
        var payslip = await check.Payslips.Include(s => s.Lines).SingleAsync();
        Assert.Equal((TaxYears.Of(today), TaxYears.MonthOf(today)), (payslip.TaxYear, payslip.TaxMonth));
        Assert.Equal([512.40m, 3000m], payslip.Lines.Select(l => l.Amount).Order());
    }

    [DatabaseFact]
    public async Task The_next_payslip_starts_as_the_month_after_the_latest()
    {
        var person = await PersonAsync();
        await using (var db = Db())
        {
            await TestData.AddPayslipAsync(db, person, 2025, 11, ("Salary", 2900m));
            await TestData.AddPayslipAsync(db, person, 2025, 12, ("Salary", 3000m), ("Tax", 500m));   // paid 25/03/2026
        }
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";

        var saved = await client.PostFormAsync(url, await client.GetAsync(url));   // nothing changed

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        await using var check = Db();
        var next = await check.Payslips.Include(s => s.Lines).SingleAsync(s => s.TaxYear == 2026);
        Assert.Equal((1, new DateOnly(2026, 4, 25)), (next.TaxMonth, next.PayDate));
        Assert.Equal([500m, 3000m], next.Lines.Select(l => l.Amount).Order());
    }

    [DatabaseFact]
    public async Task Hidden_elements_are_left_off_new_payslips()
    {
        var person = await PersonAsync();
        await using (var db = Db())
        {
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m), ("Employer pension", 100m));
            await db.PayElements.Where(el => el.Name == "Employer pension").ExecuteUpdateAsync(s => s.SetProperty(el => el.Hidden, true));
        }

        var form = await app.ClientFor(UserId).GetAsync($"/People/{person.Id}/Payslips/Create");

        Assert.DoesNotContain("Employer pension", form.Text);
        Assert.DoesNotContain(Line(4), form.Html);
    }

    [DatabaseFact]
    public async Task A_tax_month_can_only_have_one_payslip()
    {
        var person = await PersonAsync();
        await using (var db = Db()) await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m));
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";

        var page = await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Payslip.TaxMonth"] = "1" });

        Assert.Contains("already has a payslip for month 1 of 2026-27", page.Errors);
    }

    [DatabaseFact]
    public async Task The_pay_date_must_be_in_the_tax_year()
    {
        var person = await PersonAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";

        var page = await client.PostFormAsync(url, await client.GetAsync(url), new()
        {
            ["Payslip.TaxYear"] = "2026", ["Payslip.TaxMonth"] = "1", ["Payslip.PayDate"] = "2026-04-05", [Line(0)] = "1"
        });

        Assert.Contains("The pay date must be in 2026-27", page.Errors);
    }

    [DatabaseFact]
    public async Task At_least_one_amount_is_needed()
    {
        var person = await PersonAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";

        var page = await client.PostFormAsync(url, await client.GetAsync(url));

        Assert.Contains("Enter at least one amount.", page.Errors);
    }

    [DatabaseFact]
    public async Task Another_persons_employer_or_pay_element_cant_be_used()
    {
        var alex = await PersonAsync("Alex");
        var sam = await PersonAsync("Sam");
        var client = app.ClientFor(UserId);
        var url = $"/People/{alex.Id}/Payslips/Create";

        var wrongEmployer = await client.PostFormAsync(url, await client.GetAsync(url), new()
        {
            ["Payslip.EmployerId"] = sam.Employers[0].Id.ToString(), [Line(0)] = "1"
        });
        var wrongElement = await client.PostFormAsync(url, await client.GetAsync(url), new()
        {
            ["Payslip.Lines[0].PayElementId"] = sam.PayElements[0].Id.ToString(), [Line(0)] = "1"
        });

        Assert.Contains("Choose one of this person's employers.", wrongEmployer.Errors);
        Assert.Contains("don't match this person's", wrongElement.Errors);
        await using var check = Db();
        Assert.False(await check.Payslips.AnyAsync());
    }

    [DatabaseFact]
    public async Task Editing_updates_adds_and_removes_amounts()
    {
        var person = await PersonAsync();
        int id;
        await using (var db = Db()) id = (await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m), ("Tax", 500m))).Id;
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Edit/{id}";

        var saved = await client.PostFormAsync(url, await client.GetAsync(url), new()
        {
            [Line(0)] = "3100", [Line(1)] = "", [Line(2)] = "210.5", ["Payslip.Notes"] = " Pay rise "
        });

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        await using var check = Db();
        var payslip = await check.Payslips.Include(s => s.Lines).ThenInclude(l => l.PayElement).SingleAsync();
        Assert.Equal(["NI 210.5", "Salary 3100"], payslip.Lines.Select(l => $"{l.PayElement!.Name} {l.Amount:0.##}").Order());
        Assert.Equal("Pay rise", payslip.Notes);
    }

    [DatabaseFact]
    public async Task A_stale_edit_is_refused_even_if_only_amounts_changed()
    {
        var person = await PersonAsync();
        int id;
        await using (var db = Db()) id = (await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m))).Id;
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Edit/{id}";
        var (tab1, tab2) = (await client.GetAsync(url), await client.GetAsync(url));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync(url, tab1, new() { [Line(0)] = "3100" })).Status);
        var stale = await client.PostFormAsync(url, tab2, new() { [Line(0)] = "3200" });

        Assert.Contains("Someone changed this payslip", stale.Errors);
        await using var check = Db();
        Assert.Equal(3100m, (await check.PayslipLines.SingleAsync()).Amount);
    }

    [DatabaseFact]
    public async Task Deleting_a_payslip_takes_its_amounts()
    {
        var person = await PersonAsync();
        int id;
        await using (var db = Db()) id = (await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m), ("Tax", 500m))).Id;
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Delete/{id}";

        var deleted = await client.PostFormAsync(url, await client.GetAsync(url));

        Assert.Equal($"/People/{person.Id}/Payslips?year=2026", deleted.Location);
        await using var check = Db();
        Assert.False(await check.Payslips.AnyAsync());
        Assert.False(await check.PayslipLines.AnyAsync());
    }

    [DatabaseFact]
    public async Task The_year_shows_each_payslips_figures_and_the_totals()
    {
        var person = await PersonAsync();
        await using (var db = Db())
        {
            await TestData.AddPayslipAsync(db, person, 2025, 12, ("Salary", 3000m), ("Tax", 500m));
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m), ("Tax", 500m), ("Pension", 150m));
            await TestData.AddPayslipAsync(db, person, 2026, 2, ("Salary", 3300m), ("Tax", 560m), ("Pension", 165m));
        }

        var page = await app.ClientFor(UserId).GetAsync($"/People/{person.Id}/Payslips");   // the latest year

        var text = page.Text;
        Assert.Contains("<option value=\"2026\" selected=\"selected\">2026-27</option>", text);
        Assert.Contains("<option value=\"2025\">2025-26</option>", text);
        Assert.Contains("2,350.00", text);       // month 1's net
        Assert.Contains("3,600.00", text);       // month 2's rise (×12)
        Assert.Contains("6,300.00", text);       // the year to date
        Assert.Contains("£63.00", text);         // the relief: 20% of 315
        Assert.DoesNotContain(">Employer pension<", text);   // not used this year, so no column
    }

    /// <summary>The table's headings in order, each marked "|" if a section separator follows it.</summary>
    private static List<string> Headings(PageResponse page) =>
        System.Text.RegularExpressions.Regex.Matches(
                System.Text.RegularExpressions.Regex.Match(page.Html, "<thead.*?</thead>", System.Text.RegularExpressions.RegexOptions.Singleline).Value,
                "<th[^>]*?(section-end)?[^>]*>([^<]*)</th>")
            .Select(m => m.Groups[2].Value + (m.Value.Contains("section-end") ? " |" : ""))
            .Where(h => h.Length > 0)
            .ToList();

    [DatabaseFact]
    public async Task The_table_is_in_sections_with_pension_percentages_in_tooltips()
    {
        var person = await PersonAsync();
        await using (var db = Db())
        {
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m), ("Tax", 500m), ("Pension", 150m), ("Employer pension", 225m));
            await TestData.AddPayslipAsync(db, person, 2026, 2, ("Salary", 3300m), ("Tax", 560m), ("Pension", 165m), ("Employer pension", 247.5m));
        }
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips?year=2026";

        var page = await client.GetAsync(url);

        Assert.Equal(
            ["Month", "Paid |", "Salary |", "Tax", "Pension |", "Employer pension |", "Gross", "Deductions |", "Net |",
             "Annual salary", "Rise", "Take-home", "Year to date", "Predicted |"],
            Headings(page));
        Assert.Contains("title=\"5.0% of salary\"", page.Text);         // the employee's pension
        Assert.Contains("title=\"7.5% of salary\"", page.Text);         // the employer's
        Assert.Contains("title=\"+10.0% on the annual salary before\"", page.Text);   // the rise
        Assert.DoesNotContain("EE pension", page.Text);

        // With a pension received date that year, it comes after the employer's columns, ending their section.
        await using (var db = Db())
            await db.Payslips.Where(s => s.TaxMonth == 2).ExecuteUpdateAsync(s => s.SetProperty(p => p.PensionReceivedDate, new DateOnly(2026, 6, 9)));
        var headings = Headings(await client.GetAsync(url));
        Assert.Equal(["Pension |", "Employer pension", "Pension received |", "Gross"], headings[4..8]);
    }

    [DatabaseFact]
    public async Task A_bonus_shows_its_share_of_salary_and_notes_show_on_the_month()
    {
        var person = await PersonAsync();
        await using (var db = Db())
        {
            db.PayElements.Add(new PayElement { PersonId = person.Id, Name = "Xmas bonus", Kind = PayElementKind.Earning, Category = PayCategory.Bonus, SortOrder = 15 });
            await db.SaveChangesAsync();
            person = await db.People.Include(p => p.Employers).Include(p => p.PayElements).SingleAsync(p => p.Id == person.Id);
            var payslip = await TestData.AddPayslipAsync(db, person, 2026, 9, ("Salary", 3000m), ("Xmas bonus", 3600m), ("Tax", 1200m));
            payslip.Notes = "Includes the Christmas bonus";
            await TestData.AddPayslipAsync(db, person, 2026, 10, ("Salary", 3000m), ("Xmas bonus", 0m), ("Tax", 600m));
            await db.SaveChangesAsync();
        }

        var page = await app.ClientFor(UserId).GetAsync($"/People/{person.Id}/Payslips?year=2026");

        Assert.Contains("title=\"10.0% of annual salary\"", page.Text);   // 3,600 of 36,000
        Assert.DoesNotContain("title=\"0.0% of annual salary\"", page.Text);  // a zero has nothing to say
        Assert.Contains("title=\"Includes the Christmas bonus\"", page.Text);
        Assert.DoesNotContain(">Notes<", page.Html);
    }

    [DatabaseFact]
    public async Task Another_users_or_persons_payslip_is_not_found()
    {
        var alex = await PersonAsync("Alex");
        var sam = await PersonAsync("Sam");
        int id;
        await using (var db = Db()) id = (await TestData.AddPayslipAsync(db, alex, 2026, 1, ("Salary", 1m))).Id;

        var stranger = app.ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/People/{alex.Id}/Payslips")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/People/{alex.Id}/Payslips/Edit/{id}")).Status);
        var me = app.ClientFor(UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"/People/{sam.Id}/Payslips/Edit/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"/People/{sam.Id}/Payslips/Delete/{id}")).Status);
    }

    [DatabaseFact]
    public async Task A_person_needs_an_employer_and_pay_elements_first()
    {
        int id;
        await using (var db = Db())
        {
            var person = new Person { Name = "New" };
            db.People.Add(person);
            await db.SaveChangesAsync();
            id = person.Id;
        }

        var page = await app.ClientFor(UserId).GetAsync($"/People/{id}/Payslips/Create");

        Assert.Contains("A payslip needs an employer and pay elements first", page.Text);
    }
}
