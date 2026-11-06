using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>Setting payslips' pension received date from a pension's contributions: made-up payslips and exports.</summary>
public class ContributionMatchingPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    // 2020-21's first three payslips (paid 25/04, 25/05, 25/06), each with 100.00 + 150.00 of pension; and the
    // contributions they arrived as: month 1 alone, then months 2 and 3 together.
    private static readonly byte[] Export = TestCsv.Export(
        TestCsv.Contribution("09/07/2020", "500.00"),
        TestCsv.Contribution("21/05/2020", "250.00"),
        TestCsv.Interest("09/06/2020", "1.00"));

    private async Task<(int PersonId, int PensionId)> PayslipsAsync(bool assign = true)
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db), "Example SIPP", PensionType.Sipp);
        for (var month = 1; month <= 3; month++)
            await TestData.AddPayslipAsync(db, person, 2020, month, ("Salary", 2000m), ("Pension", 100m), ("Employer pension", 150m));
        if (assign) await AssignAsync(pension.Id);
        return (person.Id, pension.Id);
    }

    private async Task AssignAsync(int pensionId)
    {
        await using var db = Db();
        await db.Payslips.ExecuteUpdateAsync(s => s.SetProperty(p => p.PensionId, pensionId));
    }

    private async Task<List<(int, DateOnly?)>> ReceivedAsync()
    {
        await using var db = Db();
        return (await db.Payslips.OrderBy(s => s.TaxMonth).Select(s => new { s.TaxMonth, s.PensionReceivedDate }).ToListAsync())
            .Select(s => (s.TaxMonth, s.PensionReceivedDate)).ToList();
    }

    private static string Url((int PersonId, int PensionId) ids, string page = "") =>
        $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Transactions{page}";

    private static async Task<PageResponse> PreviewAsync(PageClient client, string url, byte[] file) =>
        await client.PostFilesAsync($"{url}?handler=Preview", await client.GetAsync(url), "uploads", [("history.csv", file)]);

    [DatabaseFact]
    public async Task Importing_sets_the_received_date_on_the_payslips_each_contribution_adds_up_to()
    {
        var ids = await PayslipsAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "/Import");

        var preview = await PreviewAsync(client, url, Export);
        Assert.Contains("Sets the pension received date on 3 payslips", preview.Text);
        Assert.Contains("21/05/2020, £250.00: 2020-21 month 1", preview.Text);
        Assert.Contains("09/07/2020, £500.00: 2020-21 months 2 and 3", preview.Text);
        Assert.Equal([(1, null), (2, null), (3, null)], await ReceivedAsync());   // nothing saved yet

        var imported = await client.PostFormAsync($"{url}?handler=Import", preview);
        Assert.Equal(HttpStatusCode.Redirect, imported.Status);
        Assert.Equal([(1, new DateOnly(2020, 5, 21)), (2, new DateOnly(2020, 7, 9)), (3, new DateOnly(2020, 7, 9))], await ReceivedAsync());

        var again = await PreviewAsync(client, url, Export);
        Assert.DoesNotContain("Sets the pension received date", again.Text);
        Assert.DoesNotContain("add up to any", again.Text);   // the matched contributions aren't offered again
    }

    [DatabaseFact]
    public async Task A_received_date_already_set_is_kept_and_its_contribution_not_matched_again()
    {
        var ids = await PayslipsAsync();
        await using (var db = Db())
            await db.Payslips.Where(s => s.TaxMonth == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.PensionReceivedDate, new DateOnly(2020, 5, 21)));
        var client = app.ClientFor(UserId);
        var url = Url(ids, "/Import");

        await client.PostFormAsync($"{url}?handler=Import", await PreviewAsync(client, url, Export));

        Assert.Equal([(1, new DateOnly(2020, 5, 21)), (2, new DateOnly(2020, 7, 9)), (3, new DateOnly(2020, 7, 9))], await ReceivedAsync());
    }

    [DatabaseFact]
    public async Task Contributions_that_match_no_payslips_are_counted()
    {
        var ids = await PayslipsAsync(assign: false);   // no payslips go to the pension

        var preview = await PreviewAsync(app.ClientFor(UserId), Url(ids, "/Import"), Export);

        Assert.Contains("2 contributions don't add up to any of the pension's payslips not yet received.", preview.Text);
        Assert.DoesNotContain("Sets the pension received date", preview.Text);
    }

    [DatabaseFact]
    public async Task Match_payslips_sets_them_from_contributions_imported_before_they_were_assigned()
    {
        var ids = await PayslipsAsync(assign: false);
        var client = app.ClientFor(UserId);
        var url = Url(ids, "/Import");
        await client.PostFormAsync($"{url}?handler=Import", await PreviewAsync(client, url, Export));
        Assert.Equal([(1, null), (2, null), (3, null)], await ReceivedAsync());
        await AssignAsync(ids.PensionId);

        var list = await client.GetAsync($"{Url(ids)}?year=2020");
        var matched = await client.PostFormAsync($"{Url(ids)}?handler=Match&year=2020", list);

        Assert.Equal($"{Url(ids)}?year=2020", matched.Location);
        Assert.Equal([(1, new DateOnly(2020, 5, 21)), (2, new DateOnly(2020, 7, 9)), (3, new DateOnly(2020, 7, 9))], await ReceivedAsync());
        Assert.Contains("Set the pension received date on 3 payslips.", (await client.GetAsync(matched.Location!)).Text);
    }
}
