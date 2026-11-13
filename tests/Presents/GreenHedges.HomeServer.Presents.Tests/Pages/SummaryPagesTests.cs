using System.Net;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;
using static GreenHedges.HomeServer.Presents.Tests.Pages.ChristmasPagesTests;

namespace GreenHedges.HomeServer.Presents.Tests.Pages;

/// <summary>The figures worked out from the presents: a Christmas's cards and totals, and History.</summary>
public class SummaryPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>The History table, as rows of cell text.</summary>
    private static List<List<string>> HistoryRows(PageResponse page) =>
        Regex.Matches(Regex.Match(page.Html, "<tbody.*?</tbody>", RegexOptions.Singleline).Value, "<tr>.*?</tr>", RegexOptions.Singleline)
            .Select(row => Regex.Matches(row.Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Select(c => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", " ")), @"\s+", " ").Trim()).ToList())
            .ToList();

    private static string YearTotal(PageResponse page) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Match(page.Html, "data-year-total>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value), @"\s+", " ").Trim();

    [DatabaseFact]
    public async Task Splits_count_their_share_and_cash_balances_to_whoever_had_most()
    {
        await using (var db = Db())
        {
            var ash = await TestData.AddChildAsync(db, "Ash", 1);
            var bea = await TestData.AddChildAsync(db, "Bea", 2);
            var christmas = await TestData.AddChristmasAsync(db, 2025);
            await TestData.AddPurchaseAsync(db, christmas, ash, "Phone", 640m, splitWithMum: true, splitWithBirthday: true);   // 160
            await TestData.AddPurchaseAsync(db, christmas, ash, "Socks", 4.99m, quantity: 2);                                  // 9.98
            await TestData.AddPurchaseAsync(db, christmas, bea, "Bike", 200m, splitWithMum: true);                            // 100
        }

        var page = await app.ClientFor(UserId).GetAsync("/2025");

        Assert.Equal(["Ash | £169.98 | £169.98 on 3 presents | No cash", "Bea | £169.98 | £100.00 on 1 present | + £69.98 cash"], Cards(page));
        Assert.Equal("£339.96 in all, £69.98 of it cash. Cash tops everyone up to whoever had most.", YearTotal(page));
        Assert.DoesNotContain("data-over-budget", page.Html);
    }

    [DatabaseFact]
    public async Task With_a_budget_everyone_is_topped_up_to_it()
    {
        await using (var db = Db())
        {
            var ash = await TestData.AddChildAsync(db, "Ash", 1);
            var bea = await TestData.AddChildAsync(db, "Bea", 2);
            var christmas = await TestData.AddChristmasAsync(db, 2025, budget: 250m);
            await TestData.AddPurchaseAsync(db, christmas, ash, price: 180m);
            await TestData.AddPurchaseAsync(db, christmas, bea, price: 240.50m);
        }

        var page = await app.ClientFor(UserId).GetAsync("/2025");

        Assert.Equal(["Ash | £250.00 | £180.00 on 1 present | + £70.00 cash", "Bea | £250.00 | £240.50 on 1 present | + £9.50 cash"], Cards(page));
        Assert.Equal("£500.00 in all, £79.50 of it cash. Cash tops everyone up to the budget.", YearTotal(page));
    }

    [DatabaseFact]
    public async Task Over_budget_it_says_who_and_tops_everyone_up_to_them()
    {
        await using (var db = Db())
        {
            var ash = await TestData.AddChildAsync(db, "Ash", 1);
            var bea = await TestData.AddChildAsync(db, "Bea", 2);
            var christmas = await TestData.AddChristmasAsync(db, 2025, budget: 250m);
            await TestData.AddPurchaseAsync(db, christmas, ash, price: 275m);
            await TestData.AddPurchaseAsync(db, christmas, bea, price: 100m);
        }

        var page = await app.ClientFor(UserId).GetAsync("/2025");

        Assert.Equal(["Ash | £275.00 | £275.00 on 1 present | No cash", "Bea | £275.00 | £100.00 on 1 present | + £175.00 cash"], Cards(page));
        Assert.Matches(@"Ash has\s+had more than the budget of £250.00, so everyone's cash tops them up to £275.00 instead.",
            Regex.Match(page.Text, "data-over-budget>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value);
    }

    [DatabaseFact]
    public async Task A_year_with_nothing_yet_has_no_totals_line()
    {
        await using (var db = Db()) await TestData.AddChildAsync(db);

        Assert.DoesNotContain("data-year-total", (await app.ClientFor(UserId).GetAsync("/2025")).Html);
    }

    [DatabaseFact]
    public async Task History_lists_every_year_latest_first_with_spent_cash_and_totals()
    {
        await using (var db = Db())
        {
            var ash = await TestData.AddChildAsync(db, "Ash", 1);
            var bea = await TestData.AddChildAsync(db, "Bea", 2);
            var older = await TestData.AddChristmasAsync(db, 2022, budget: 100m);
            await TestData.AddPurchaseAsync(db, older, ash, price: 60m);
            await TestData.AddPurchaseAsync(db, older, bea, price: 90m);
            var newer = await TestData.AddChristmasAsync(db, 2023);
            await TestData.AddPurchaseAsync(db, newer, ash, price: 50m);
            await TestData.AddPurchaseAsync(db, newer, bea, price: 80m, splitWithBirthday: true);   // 40
        }

        var page = await app.ClientFor(UserId).GetAsync("/History");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Matches(@"<th colspan=""2""[^>]*>Ash</th>\s*<th colspan=""2""[^>]*>Bea</th>", page.Html);
        Assert.Equal([
            ["2023", "£50.00", "–", "£40.00", "£10.00", "£50.00", "£100.00"],
            ["2022 £100", "£60.00", "£40.00", "£90.00", "£10.00", "£100.00", "£200.00"]], HistoryRows(page));
        Assert.Contains("href=\"/2023\"", page.Html);
    }

    [DatabaseFact]
    public async Task History_with_no_Christmases_says_so() =>
        Assert.Contains("No Christmases yet", (await app.ClientFor(UserId).GetAsync("/History")).Text);

    [DatabaseFact]
    public async Task History_shows_only_the_users_own_years()
    {
        await using (var other = TestDatabase.As(NewUser()))
            await TestData.AddPurchaseAsync(other, await TestData.AddChristmasAsync(other, 2021), await TestData.AddChildAsync(other, "Cal"));

        var page = await app.ClientFor(UserId).GetAsync("/History");

        Assert.DoesNotContain("2021", page.Html);
        Assert.DoesNotContain("Cal", page.Html);
    }
}
