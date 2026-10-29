using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>Importing made-up workbooks (<see cref="TestWorkbook"/>) through the upload, preview and import pages.</summary>
public class ImportPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>Three months of 2026-27, a month laid out but not paid yet, and an Adj in both earnings and deductions.</summary>
    private static byte[] Workbook() => new TestWorkbook(["Salary", "Adj"], ["Tax", "NI", "EE Pension", "Adj"], ["ER Pension"])
        {
            BeforeGross = ["Transfer Date"], AfterNet = ["Notes"]
        }
        .Row(2027, 1, new DateOnly(2026, 4, 24), 3000, null, 500, 200, 150, null, 225, null, null)
        .Row(2027, 2, new DateOnly(2026, 5, 22), 3000, 25, 505, 202, 150, 5, 225, new DateOnly(2026, 6, 9), "Commission")
        .Row(2027, 3, new DateOnly(2026, 6, 25), 3300, null, 560, 220, 165, null, 250, null, null)
        .Row(2027, 4, null)
        .Bytes();

    private async Task<int> PersonAsync(bool withElements = false)
    {
        await using var db = Db();
        if (withElements) return (await TestData.AddPersonAsync(db)).Id;
        var person = new Person { Name = "Alex" };
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    private async Task<PageResponse> PreviewAsync(PageClient client, int personId, byte[] workbook)
    {
        var url = $"/People/{personId}/Import";
        return await client.PostFileAsync($"{url}?handler=Preview", await client.GetAsync(url), "upload", "Payslips.xlsx", workbook);
    }

    private static Task<PageResponse> ImportAsync(PageClient client, int personId, PageResponse preview, Dictionary<string, string>? fields = null) =>
        client.PostFormAsync($"/People/{personId}/Import?handler=Import", preview, fields);

    [DatabaseFact]
    public async Task The_preview_shows_what_would_be_imported_and_that_it_matches_the_sheet()
    {
        var personId = await PersonAsync();
        var preview = await PreviewAsync(app.ClientFor(UserId), personId, Workbook());

        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Contains("3</strong> payslips to import: April 2026-27 to June 2026-27", preview.Text);
        Assert.Contains("Every row's gross and net match the spreadsheet's own", preview.Text);
        Assert.Contains("value=\"Adj (deduction)\"", preview.Html);
        await using var check = Db();
        Assert.False(await check.Payslips.AnyAsync());   // nothing saved yet
    }

    [DatabaseFact]
    public async Task Importing_makes_the_pay_elements_employer_and_payslips()
    {
        var personId = await PersonAsync();
        var client = app.ClientFor(UserId);
        var preview = await PreviewAsync(client, personId, Workbook());

        var imported = await ImportAsync(client, personId, preview, new() { ["EmployerId"] = "0", ["NewEmployerName"] = "Example Ltd" });

        Assert.Equal(HttpStatusCode.Redirect, imported.Status);
        Assert.Equal($"/People/{personId}/Payslips?year=2026", imported.Location);
        await using var check = Db();
        Assert.Equal(["Salary Earning Salary", "Adj Earning Other", "Tax Deduction Tax", "NI Deduction NationalInsurance",
                      "EE Pension Deduction Pension", "Adj (deduction) Deduction Other", "ER Pension EmployerContribution Pension"],
            await check.PayElements.OrderBy(el => el.SortOrder).Select(el => $"{el.Name} {el.Kind} {el.Category}").ToListAsync());
        var payslips = await check.Payslips.Include(s => s.Lines).Include(s => s.Employer).OrderBy(s => s.TaxMonth).ToListAsync();
        Assert.Equal([1, 2, 3], payslips.Select(s => s.TaxMonth));
        Assert.All(payslips, s => Assert.Equal(("Example Ltd", 2026), (s.Employer!.Name, s.TaxYear)));
        Assert.Equal([5, 7, 5], payslips.Select(s => s.Lines.Count));
        Assert.Equal((new DateOnly(2026, 6, 9), "Commission"), (payslips[1].PensionReceivedDate, payslips[1].Notes));
    }

    [DatabaseFact]
    public async Task Importing_again_adds_only_new_months_and_uses_the_existing_pay_elements()
    {
        var personId = await PersonAsync(withElements: true);   // Salary, Tax, NI, Pension, Employer pension; Example Ltd
        await using (var db = Db())
        {
            var person = await db.People.Include(p => p.Employers).Include(p => p.PayElements).SingleAsync();
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m));
        }
        var client = app.ClientFor(UserId);
        var workbook = new TestWorkbook(["Salary"], ["Tax", "NI"])
            .Row(2027, 1, new DateOnly(2026, 4, 24), 3000, 500, 200)
            .Row(2027, 2, new DateOnly(2026, 5, 22), 3000, 500, 200)
            .Bytes();

        var preview = await PreviewAsync(client, personId, workbook);
        Assert.Contains("1 months Alex already has payslips for", preview.Text);
        var imported = await ImportAsync(client, personId, preview);   // the existing employer is chosen

        Assert.Equal(HttpStatusCode.Redirect, imported.Status);
        await using var check = Db();
        Assert.Equal(5, await check.PayElements.CountAsync());
        Assert.Equal(1m, (await check.PayslipLines.SingleAsync(l => l.Payslip!.TaxMonth == 1)).Amount);   // left as it was
        Assert.Equal(3, await check.PayslipLines.CountAsync(l => l.Payslip!.TaxMonth == 2));
    }

    [DatabaseFact]
    public async Task A_column_can_be_renamed_to_one_of_the_persons_pay_elements()
    {
        var personId = await PersonAsync(withElements: true);
        var client = app.ClientFor(UserId);
        var preview = await PreviewAsync(client, personId, new TestWorkbook(["Salary"], ["Tax", "EE Pension"])
            .Row(2027, 1, new DateOnly(2026, 4, 24), 3000, 500, 150).Bytes());

        await ImportAsync(client, personId, preview, new() { ["Columns[2].Name"] = "pension" });

        await using var check = Db();
        Assert.Equal(5, await check.PayElements.CountAsync());   // "pension" is their Pension
        Assert.Equal(150m, (await check.PayslipLines.SingleAsync(l => l.PayElement!.Name == "Pension")).Amount);
    }

    [DatabaseFact]
    public async Task Columns_given_the_same_name_are_refused()
    {
        var personId = await PersonAsync();
        var client = app.ClientFor(UserId);
        var preview = await PreviewAsync(client, personId, Workbook());

        var page = await ImportAsync(client, personId, preview, new() { ["Columns[3].Name"] = "Tax", ["EmployerId"] = "0", ["NewEmployerName"] = "X" });

        Assert.Contains("Columns F and G would both be \"Tax\"", page.Errors);   // NI (G) renamed to Tax (F)
        await using var check = Db();
        Assert.False(await check.Payslips.AnyAsync());
    }

    [DatabaseFact]
    public async Task A_new_employer_needs_a_name()
    {
        var personId = await PersonAsync();
        var client = app.ClientFor(UserId);

        var page = await ImportAsync(client, personId, await PreviewAsync(client, personId, Workbook()), new() { ["EmployerId"] = "0" });

        Assert.Contains("Name the new employer", page.Errors);
    }

    [DatabaseFact]
    public async Task Rows_that_cant_be_imported_and_totals_that_dont_match_are_shown()
    {
        var personId = await PersonAsync();
        // No formulas, and Pension before Tax, so it's taken for an earning and the gross is out.
        var workbook = new TestWorkbook(["Salary"], ["Pension", "Tax"]) { NoFormulas = true }
            .Row(2018, 2, new DateOnly(2017, 5, 25), 3000, 150, 500)
            .Row(2018, 3, new DateOnly(2003, 6, 27), 3000, 150, 500)
            .Bytes();

        var preview = await PreviewAsync(app.ClientFor(UserId), personId, workbook);

        Assert.Contains("Row 3 is skipped: the date 27/06/2003 isn't in 2017-18", preview.Text);
        Assert.Contains("1 rows don't match the spreadsheet's own totals", preview.Text);
        Assert.Contains("row 2: gross 3,150.00, the sheet's 3,000.00", preview.Text);
    }

    [DatabaseFact]
    public async Task The_error_box_only_shows_when_there_is_an_error()
    {
        var personId = await PersonAsync();
        var client = app.ClientFor(UserId);

        Assert.DoesNotContain("alert-danger", (await client.GetAsync($"/People/{personId}/Import")).Html);
        Assert.Contains("alert-danger", (await PreviewAsync(client, personId, "not a spreadsheet"u8.ToArray())).Html);
    }

    [DatabaseFact]
    public async Task A_file_that_isnt_a_workbook_is_refused()
    {
        var personId = await PersonAsync();

        var page = await PreviewAsync(app.ClientFor(UserId), personId, "not a spreadsheet"u8.ToArray());

        Assert.Contains("That isn't a spreadsheet this can read", page.Errors);
    }

    [DatabaseFact]
    public async Task Nothing_new_to_import_says_so()
    {
        var personId = await PersonAsync(withElements: true);
        await using (var db = Db())
        {
            var person = await db.People.Include(p => p.Employers).Include(p => p.PayElements).SingleAsync();
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m));
        }
        var client = app.ClientFor(UserId);
        var preview = await PreviewAsync(client, personId, new TestWorkbook(["Salary"], ["Tax"])
            .Row(2027, 1, new DateOnly(2026, 4, 24), 3000, 500).Bytes());

        var page = await ImportAsync(client, personId, preview);

        Assert.Contains("already has a payslip for every month in this workbook", page.Errors);
    }

    [DatabaseFact]
    public async Task Another_users_person_is_not_found()
    {
        var personId = await PersonAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync($"/People/{personId}/Import")).Status);
    }
}
