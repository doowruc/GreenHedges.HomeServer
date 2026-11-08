using Microsoft.EntityFrameworkCore;
using System.Net;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>The dashboard and the tax years page, with made-up payslips and pensions.</summary>
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

    /// <summary>A SIPP with a fund bought at 200p and now valued at its last deal, 300p, and two statements with a projection.</summary>
    private async Task<Pension> AddSippAsync(Person person, AppDbContext db)
    {
        var sipp = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db), "Example SIPP", PensionType.Sipp);
        var type = await TestData.AddStatementTypeAsync(db, sipp, "Annual illustration", hasProjections: true);
        await TestData.AddStatementAsync(db, type, new DateOnly(2025, 4, 5), 900m, projectedFund: 40000m, projectedAnnualIncome: 1600m);
        await TestData.AddStatementAsync(db, type, new DateOnly(2026, 4, 5), 950m, projectedFund: 50000m, projectedAnnualIncome: 2000m);
        var bought = await TestData.AddTransactionAsync(db, sipp, new DateOnly(2026, 5, 1), -600m, TransactionKind.Purchase, "B1", "Fund 300 @ 200");
        (bought.Fund, bought.Quantity, bought.UnitPrice) = ("Fund", 300m, 200m);
        var more = await TestData.AddTransactionAsync(db, sipp, new DateOnly(2026, 6, 1), -300m, TransactionKind.Purchase, "B2", "Fund 100 @ 300");
        (more.Fund, more.Quantity, more.UnitPrice) = ("Fund", 100m, 300m);
        await db.SaveChangesAsync();
        return sipp;
    }

    [DatabaseFact]
    public async Task The_dashboard_sums_up_the_persons_pensions()
    {
        var person = await PersonWithPayAsync();
        Pension sipp;
        await using (var db = Db())
        {
            sipp = await AddSippAsync(person, db);
            // Valued from a statement alone: in the total and the table, not the gain.
            var workplace = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db, "Other Pensions"), "Example workplace");
            await TestData.AddStatementAsync(db, await TestData.AddStatementTypeAsync(db, workplace), new DateOnly(2026, 3, 31), 500m);
            await TestData.AddPayslipAsync(db, person, 2026, 3, ("Salary", 3300m), ("Pension", 165m), ("Employer pension", 200m));
        }

        var page = await app.ClientFor(UserId).GetAsync("/");

        Assert.Contains("£1,700.00", page.Text);               // 400 units at 300p, and the statement
        Assert.Contains("2 pensions, oldest figure 31/03/2026", page.Text);
        Assert.Contains("+£300.00", page.Text);                // 1,200 less 900
        Assert.Contains("+33.33% on £900.00 cost, pensions with holdings only", page.Text);
        Assert.Contains("£695.00", page.Text);                 // 2026-27: 3 × 165 yours and 200 employer
        Assert.Contains("£495.00 yours, £200.00 employer; 2025-26 was £1,800.00", page.Text);
        Assert.Contains("£50,000.00", page.Text);
        Assert.Contains("£2,000.00 a year", page.Text);
        Assert.Contains("data-pension-values=", page.Html);
        Assert.Contains("Example workplace", page.Text);
        Assert.Contains($"href=\"/People/{person.Id}/Pensions/{sipp.Id}/Holdings\"", page.Html);
        Assert.Contains($"href=\"/People/{person.Id}/Pensions/{sipp.Id}/Statements\"", page.Html);
    }

    [DatabaseFact]
    public async Task Someone_with_a_pension_but_no_payslips_still_sees_it()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        await AddSippAsync(person, db);

        var page = await app.ClientFor(UserId).GetAsync($"/?person={person.Id}");

        Assert.Contains("Alex has no payslips yet", page.Text);
        Assert.Contains("£1,200.00", page.Text);
        Assert.DoesNotContain("contributions", page.Text);
        Assert.Contains("data-pension-values=", page.Html);
        Assert.Contains("/js/pay-charts", page.Html);          // the chart has its script without the pay charts
    }

    [DatabaseFact]
    public async Task A_pension_with_only_one_value_gets_no_chart_and_a_pension_with_none_is_still_listed()
    {
        var person = await PersonWithPayAsync();
        await using (var db = Db())
        {
            var provider = await TestData.AddProviderAsync(db);
            var one = await TestData.AddPensionAsync(db, person, provider, "One statement");
            await TestData.AddStatementAsync(db, await TestData.AddStatementTypeAsync(db, one), new DateOnly(2026, 3, 31), 500m);
            await TestData.AddPensionAsync(db, person, provider, "Brand new");
        }

        var page = await app.ClientFor(UserId).GetAsync("/");

        Assert.Contains("One statement", page.Text);
        Assert.Contains("Brand new", page.Text);
        Assert.Contains("as at 31/03/2026", page.Text);
        Assert.DoesNotContain("data-pension-values=", page.Html);
    }

    [DatabaseFact]
    public async Task Without_pensions_the_dashboard_has_no_pension_section()
    {
        await PersonWithPayAsync();

        var page = await app.ClientFor(UserId).GetAsync("/");

        Assert.DoesNotContain("Each pension", page.Text);
        Assert.DoesNotContain("data-pension-values=", page.Html);
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
    public async Task Salary_sacrifice_pension_is_left_out_of_the_relief_on_the_tax_year_pages()
    {
        var person = await PersonWithPayAsync();
        await using (var db = Db())
            await db.PayElements.Where(el => el.Name == "Pension").ExecuteUpdateAsync(s => s.SetProperty(el => el.SalarySacrifice, true));
        var client = app.ClientFor(UserId);

        var years = await client.GetAsync($"/People/{person.Id}/TaxYears");
        var payslips = await client.GetAsync($"/People/{person.Id}/Payslips?year=2025");

        Assert.Contains("36,000.00", years.Text);
        Assert.DoesNotContain("360.00", years.Text);             // no relief on 2025-26's 1,800 of pension
        Assert.Contains("Higher-rate pension relief", payslips.Text);
        Assert.DoesNotContain("£360.00", payslips.Text);
    }

    [DatabaseFact]
    public async Task Another_users_tax_years_are_not_found()
    {
        var person = await PersonWithPayAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync($"/People/{person.Id}/TaxYears")).Status);
    }
}
