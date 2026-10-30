using System.Net;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>What every page shares: the navbar's people switcher and Bootstrap tooltips.</summary>
public class LayoutTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>The switcher's people, as "name → link", the current one marked "*".</summary>
    private static List<string> Switcher(PageResponse page) =>
        Regex.Matches(Regex.Match(page.Html, "<ul class=\"navbar-nav nav-pills person-switcher\".*?</ul>", RegexOptions.Singleline).Value,
                "<a href=\"([^\"]*)\" class=\"nav-link[^\"]*?( active)?\"[^>]*>([^<]*)</a>")
            .Select(m => $"{m.Groups[3].Value} → {WebUtility.HtmlDecode(m.Groups[1].Value)}{(m.Groups[2].Success ? " *" : "")}")
            .ToList();

    private async Task<(int Alex, int Sam, int AlexPayslip)> TwoPeopleAsync()
    {
        await using var db = Db();
        var alex = await TestData.AddPersonAsync(db, "Alex");
        var sam = await TestData.AddPersonAsync(db, "Sam");
        var payslip = await TestData.AddPayslipAsync(db, alex, 2025, 3, ("Salary", 3000m));
        return (alex.Id, sam.Id, payslip.Id);
    }

    [DatabaseFact]
    public async Task A_persons_page_switches_to_the_same_page_for_the_other_person()
    {
        var (alex, sam, _) = await TwoPeopleAsync();
        var client = app.ClientFor(UserId);

        Assert.Equal([$"Alex → /People/{alex}/Payslips?year=2025 *", $"Sam → /People/{sam}/Payslips?year=2025"],
            Switcher(await client.GetAsync($"/People/{alex}/Payslips?year=2025")));
        Assert.Equal([$"Alex → /People/{alex}/TaxYears", $"Sam → /People/{sam}/TaxYears *"],
            Switcher(await client.GetAsync($"/People/{sam}/TaxYears")));
        Assert.Equal([$"Alex → /People/{alex}/Payslips/Create *", $"Sam → /People/{sam}/Payslips/Create"],
            Switcher(await client.GetAsync($"/People/{alex}/Payslips/Create")));
    }

    [DatabaseFact]
    public async Task An_items_page_switches_to_the_other_persons_list()
    {
        var (alex, sam, payslip) = await TwoPeopleAsync();

        var switcher = Switcher(await app.ClientFor(UserId).GetAsync($"/People/{alex}/Payslips/Edit/{payslip}"));

        Assert.Equal($"Sam → /People/{sam}/Payslips", switcher[1]);
    }

    [DatabaseFact]
    public async Task Other_pages_switch_to_the_persons_dashboard()
    {
        var (alex, sam, _) = await TwoPeopleAsync();
        var client = app.ClientFor(UserId);

        Assert.Equal([$"Alex → /?person={alex}", $"Sam → /?person={sam}"], Switcher(await client.GetAsync("/People")));
        // The dashboard shows the first person with payslips, and the switcher says so.
        Assert.Equal([$"Alex → /?person={alex} *", $"Sam → /?person={sam}"], Switcher(await client.GetAsync("/")));
    }

    [DatabaseFact]
    public async Task With_one_person_there_is_nothing_to_switch_to()
    {
        await using (var db = Db()) await TestData.AddPersonAsync(db, "Alex");
        Assert.DoesNotContain("person-switcher", (await app.ClientFor(UserId).GetAsync("/People")).Html);
    }

    [DatabaseFact]
    public async Task Titles_become_Bootstrap_tooltips() =>
        Assert.Matches("<script src=\"/js/tooltips[^\"]*\\.js", (await app.ClientFor(UserId).GetAsync("/People")).Html);

    [DatabaseFact]
    public async Task Signed_out_pages_have_no_switcher()
    {
        await TwoPeopleAsync();
        var page = await app.ClientFor(null).GetAsync("/Account/SignedOut");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.DoesNotContain("person-switcher", page.Html);
    }
}
