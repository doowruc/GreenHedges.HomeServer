using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>
/// Payslips' pensions: on the payslip form, in the payslips table, and assigned to a range at once. The test person's
/// form lines are Salary (0), Tax (1), NI (2), Pension (3), Employer pension (4).
/// </summary>
public class PayslipPensionTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static string Line(int index) => $"Payslip.Lines[{index}].Amount";

    /// <summary>Alex, with two pensions with one provider: Workplace pension and Old pension.</summary>
    private async Task<(Person Person, int Workplace, int Old)> PersonAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var provider = await TestData.AddProviderAsync(db);
        var workplace = await TestData.AddPensionAsync(db, person, provider);
        var old = await TestData.AddPensionAsync(db, person, provider, "Old pension");
        return (person, workplace.Id, old.Id);
    }

    private async Task<int> PayslipAsync(Person person, int taxYear, int taxMonth, int? pensionId, bool withPension = true)
    {
        await using var db = Db();
        var payslip = withPension
            ? await TestData.AddPayslipAsync(db, person, taxYear, taxMonth, ("Salary", 3000m), ("Pension", 150m))
            : await TestData.AddPayslipAsync(db, person, taxYear, taxMonth, ("Salary", 3000m));
        if (pensionId is not null) await db.Payslips.Where(s => s.Id == payslip.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.PensionId, pensionId));
        return payslip.Id;
    }

    private async Task<Dictionary<int, int?>> PensionsByMonthAsync()
    {
        await using var check = Db();
        return await check.Payslips.ToDictionaryAsync(s => s.TaxYear * 100 + s.TaxMonth, s => s.PensionId);
    }

    [DatabaseFact]
    public async Task A_payslip_saves_its_pension_and_the_next_one_starts_with_it()
    {
        var (person, workplace, _) = await PersonAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";

        var form = await client.GetAsync(url);
        Assert.Equal(workplace.ToString(), form.OptionValue("Workplace pension (Example Pensions)"));
        var saved = await client.PostFormAsync(url, form, new() { [Line(0)] = "3000", [Line(3)] = "150", ["Payslip.PensionId"] = workplace.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        await client.PostFormAsync(url, await client.GetAsync(url));   // the next month, unchanged

        await using var check = Db();
        Assert.Equal([workplace, workplace], await check.Payslips.OrderBy(s => s.TaxYear).ThenBy(s => s.TaxMonth).Select(s => s.PensionId).ToListAsync());
    }

    [DatabaseFact]
    public async Task Only_a_payslip_with_a_pension_amount_can_have_a_pension()
    {
        var (person, workplace, _) = await PersonAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";

        var page = await client.PostFormAsync(url, await client.GetAsync(url), new() { [Line(0)] = "3000", ["Payslip.PensionId"] = workplace.ToString() });

        Assert.Contains("Only a payslip with a pension amount can have a pension", page.Errors);
        await using var check = Db();
        Assert.False(await check.Payslips.AnyAsync());
    }

    [DatabaseFact]
    public async Task Another_persons_pension_is_refused()
    {
        var (person, _, _) = await PersonAsync();
        int samsPension;
        await using (var db = Db())
            samsPension = (await TestData.AddPensionAsync(db, await TestData.AddPersonAsync(db, "Sam"), await db.PensionProviders.SingleAsync())).Id;
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Create";

        var page = await client.PostFormAsync(url, await client.GetAsync(url),
            new() { [Line(0)] = "3000", [Line(3)] = "150", ["Payslip.PensionId"] = samsPension.ToString() });

        Assert.Contains("Choose one of this person's pensions", page.Errors);
    }

    [DatabaseFact]
    public async Task An_edit_can_change_or_clear_the_pension()
    {
        var (person, workplace, old) = await PersonAsync();
        var id = await PayslipAsync(person, 2026, 1, workplace);
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Payslips/Edit/{id}";

        await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Payslip.PensionId"] = old.ToString() });
        Assert.Equal(old, (await PensionsByMonthAsync())[202601]);
        await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Payslip.PensionId"] = "" });
        Assert.Null((await PensionsByMonthAsync())[202601]);
    }

    [DatabaseFact]
    public async Task The_table_shows_a_pension_column_only_when_a_years_pensions_differ()
    {
        var (person, workplace, old) = await PersonAsync();
        await PayslipAsync(person, 2025, 1, old);
        await PayslipAsync(person, 2025, 2, workplace);
        await PayslipAsync(person, 2026, 1, workplace);
        await PayslipAsync(person, 2026, 2, workplace);
        await PayslipAsync(person, 2026, 3, null, withPension: false);   // no pension line: doesn't count
        var client = app.ClientFor(UserId);

        var differ = await client.GetAsync($"/People/{person.Id}/Payslips?year=2025");
        var same = await client.GetAsync($"/People/{person.Id}/Payslips?year=2026");

        Assert.Contains("<th class=\"section-end\">Pension</th>", differ.Html);
        Assert.Contains("<td class=\"text-nowrap section-end\">Old pension</td>", differ.Html);
        Assert.DoesNotContain("<th class=\"section-end\">Pension</th>", same.Html);   // not the "Pension" pay element's (text-end)
        Assert.DoesNotContain("<th>Pension</th>", same.Html);
    }

    [DatabaseFact]
    public async Task Assigning_sets_the_pension_on_the_ranges_payslips_with_pension_lines_after_saying_how_many()
    {
        var (person, workplace, old) = await PersonAsync();
        await PayslipAsync(person, 2024, 12, null);                       // before the range
        await PayslipAsync(person, 2025, 1, null);
        await PayslipAsync(person, 2025, 2, old);                         // replaced
        await PayslipAsync(person, 2025, 3, workplace);                   // already set
        await PayslipAsync(person, 2025, 4, null, withPension: false);    // no pension line
        await PayslipAsync(person, 2025, 5, null);
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Pensions/Assign/{workplace}";
        var range = new Dictionary<string, string> { ["FromYear"] = "2025", ["FromMonth"] = "1", ["ToYear"] = "2025", ["ToMonth"] = "5" };

        var check = await client.PostFormAsync(url, await client.GetAsync(url), range);
        Assert.Contains("on 3 payslips from April 2025-26 to August 2025-26, replacing another pension on 1 (1 already have it)",
            System.Text.RegularExpressions.Regex.Replace(check.Text, "<[^>]+>|\\s+", m => m.Value.StartsWith('<') ? "" : " "));
        Assert.Null((await PensionsByMonthAsync())[202501]);   // nothing changed yet

        var done = await client.PostFormAsync(url, check, new(range) { ["Confirm"] = "true" });

        Assert.Equal(HttpStatusCode.Redirect, done.Status);
        Assert.Equal($"/People/{person.Id}/Pensions", done.Location);
        var pensions = await PensionsByMonthAsync();
        Assert.Equal([null, workplace, workplace, workplace, null, workplace],
            new[] { 202412, 202501, 202502, 202503, 202504, 202505 }.Select(m => pensions[m]));
    }

    [DatabaseFact]
    public async Task Assigning_checks_again_if_the_range_changed_after_checking()
    {
        var (person, workplace, _) = await PersonAsync();
        await PayslipAsync(person, 2025, 1, null);
        await PayslipAsync(person, 2025, 2, null);
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Pensions/Assign/{workplace}";

        var check = await client.PostFormAsync(url, await client.GetAsync(url),
            new() { ["FromYear"] = "2025", ["FromMonth"] = "1", ["ToYear"] = "2025", ["ToMonth"] = "1" });
        var page = await client.PostFormAsync(url, check, new() { ["ToMonth"] = "2", ["Confirm"] = "true" });

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Assign to 2 payslips", page.Text);
        Assert.All((await PensionsByMonthAsync()).Values, Assert.Null);
    }

    [DatabaseFact]
    public async Task Assigning_starts_from_the_persons_payslips_with_pension_lines_and_refuses_a_backwards_range()
    {
        var (person, workplace, _) = await PersonAsync();
        await PayslipAsync(person, 2023, 1, null, withPension: false);
        await PayslipAsync(person, 2023, 4, null);
        await PayslipAsync(person, 2025, 7, null);
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Pensions/Assign/{workplace}";

        var form = await client.GetAsync(url);
        Assert.Matches("name=\"FromYear\"[^>]*>.*?<option selected=\"selected\" value=\"2023\">", form.Html.Replace("\n", ""));
        Assert.Matches("name=\"FromMonth\"[^>]*>.*?<option selected=\"selected\" value=\"4\">", form.Html.Replace("\n", ""));
        Assert.Matches("name=\"ToMonth\"[^>]*>.*?<option selected=\"selected\" value=\"7\">", form.Html.Replace("\n", ""));

        var backwards = await client.PostFormAsync(url, form, new() { ["FromYear"] = "2025", ["ToYear"] = "2023" });
        Assert.Contains("The first month must be before the last", backwards.Errors);
    }

    [DatabaseFact]
    public async Task A_pension_with_payslips_cant_be_deleted()
    {
        var (person, workplace, _) = await PersonAsync();
        await PayslipAsync(person, 2026, 1, workplace);
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Pensions/Delete/{workplace}";

        var page = await client.GetAsync(url);
        Assert.Contains("can't be deleted while payslips go to it (1)", page.Text);
        await client.PostFormAsync(url, page);

        await using var check = Db();
        Assert.True(await check.Pensions.AnyAsync(pn => pn.Id == workplace));
    }

    [DatabaseFact]
    public async Task Another_persons_pension_cant_be_assigned_from_this_person()
    {
        var (_, workplace, _) = await PersonAsync();
        int samId;
        await using (var db = Db()) samId = (await TestData.AddPersonAsync(db, "Sam")).Id;

        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync($"/People/{samId}/Pensions/Assign/{workplace}")).Status);
    }
}
