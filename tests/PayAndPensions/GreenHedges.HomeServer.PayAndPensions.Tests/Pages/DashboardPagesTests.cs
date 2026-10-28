using System.Net;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>The dashboard and the tax years page, with made-up payslips.</summary>
public class DashboardPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>A full 2025-26 at 3,000 a month, and two months of 2026-27 at 3,300.</summary>
    private async Task<Person> PersonWithPayAsync(string name = "Alex")
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db, name);
        for (var month = 1; month <= 12; month++)
            await TestData.AddPayslipAsync(db, person, 2025, month, ("Salary", 3000m), ("Tax", 500m), ("Pension", 150m));
        await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3300m), ("Tax", 560m), ("Pension", 165m));
        await TestData.AddPayslipAsync(db, person, 2026, 2, ("Salary", 3300m), ("Tax", 560m), ("Pension", 165m));
        return person;
    }

    [DatabaseFact]
    public async Task The_dashboard_shows_the_latest_payslip_and_the_year_so_far()
    {
        await PersonWithPayAsync();

        var text = (await app.ClientFor(UserId).GetAsync("/")).Text;

        Assert.Contains("£2,575.00", text);                  // the latest net
        Assert.Contains("May 2026-27, paid 25/05/2026", text);
        Assert.Contains("£39,600.00", text);                 // annual salary
        Assert.Contains("£6,600.00", text);                  // 2026-27 so far
        Assert.Contains("2025-26 was £36,000.00", text);
        Assert.Contains("data-pay-monthly=", text);
        Assert.Contains("data-pay-yearly=", text);
    }

    [DatabaseFact]
    public async Task The_dashboard_shows_one_person_at_a_time()
    {
        var alex = await PersonWithPayAsync("Alex");
        int samId;
        await using (var db = Db()) samId = (await TestData.AddPersonAsync(db, "Sam")).Id;   // no payslips
        var client = app.ClientFor(UserId);

        var first = await client.GetAsync("/");               // the first person with payslips
        var sam = await client.GetAsync($"/?person={samId}");

        Assert.Contains("£2,575.00", first.Text);
        Assert.Contains("Sam has no payslips yet", sam.Text);
        Assert.Contains($"href=\"/People/{samId}/Import\"", sam.Html);
        Assert.Contains($"href=\"/?person={alex.Id}\"", sam.Html);
        // Someone else, with people of their own, can't pick Alex; with none, they're only welcomed.
        var stranger = NewUser();
        Assert.DoesNotContain("£2,575.00", (await app.ClientFor(stranger).GetAsync($"/?person={alex.Id}")).Text);
        await using (var db = TestDatabase.As(stranger)) await TestData.AddPersonAsync(db, "Someone");
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(stranger).GetAsync($"/?person={alex.Id}")).Status);
    }

    [DatabaseFact]
    public async Task With_nobody_yet_the_dashboard_says_where_to_start()
    {
        var text = (await app.ClientFor(UserId).GetAsync("/")).Text;
        Assert.Contains("Start by", text);
    }

    [DatabaseFact]
    public async Task The_tax_years_page_lists_each_year_newest_first_with_its_change()
    {
        var person = await PersonWithPayAsync();

        var page = await app.ClientFor(UserId).GetAsync($"/People/{person.Id}/TaxYears");

        Assert.True(page.Text.IndexOf(">2026-27</a>") is > 0 and var newest && newest < page.Text.IndexOf(">2025-26</a>"));
        Assert.Contains("36,000.00", page.Text);              // 2025-26 gross
        Assert.Contains("360.00", page.Text);                 // its pension relief: 20% of 1,800
        Assert.Contains("+600.00 (10.0%)", page.Text);        // 2026-27 so far against April and May of 2025-26
        Assert.Contains("data-pay-yearly=", page.Text);
    }

    [DatabaseFact]
    public async Task Another_users_tax_years_are_not_found()
    {
        var person = await PersonWithPayAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync($"/People/{person.Id}/TaxYears")).Status);
    }
}
