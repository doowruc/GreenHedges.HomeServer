using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Presents.Tests.Pages;

/// <summary>A Christmas's page (this year's at /), moving between years, and a year's budget and deletion.</summary>
public class ChristmasPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly int ThisYear = DateTime.Today.Year;

    /// <summary>Each child's card as "name | total | spent | cash", in page order.</summary>
    internal static List<string> Cards(PageResponse page) =>
        Regex.Matches(page.Html, "data-child-summary>(.*?)</div></div>", RegexOptions.Singleline)
            .Select(card => string.Join(" | ", Regex.Matches(card.Groups[1].Value, "<div[^>]*>(.*?)</div>", RegexOptions.Singleline)
                .Select(d => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(d.Groups[1].Value, "<[^>]+>", " ")), @"\s+", " ").Trim())))
            .ToList();

    private static List<string> Years(PageResponse page) =>
        Regex.Matches(Regex.Match(page.Html, "data-years>(.*?)</ul>", RegexOptions.Singleline).Groups[1].Value, ">([^<]+)</a>")
            .Select(m => m.Groups[1].Value).ToList();

    [DatabaseFact]
    public async Task With_no_children_it_says_to_add_one_and_has_no_form()
    {
        var page = await app.ClientFor(UserId).GetAsync("/");

        Assert.Contains($"Christmas {ThisYear}", page.Text);
        Assert.Contains("href=\"/Children/Create\"", page.Html);
        Assert.DoesNotContain("Add a present", page.Html);
    }

    [DatabaseFact]
    public async Task Each_child_has_a_card_in_order_with_that_years_spend_cash_and_total()
    {
        await using (var db = Db())
        {
            var bea = await TestData.AddChildAsync(db, "Bea", 2);
            var ash = await TestData.AddChildAsync(db, "Ash", 1);
            var thisYear = await TestData.AddChristmasAsync(db, ThisYear);
            var lastYear = await TestData.AddChristmasAsync(db, ThisYear - 1);
            await TestData.AddPurchaseAsync(db, thisYear, bea, quantity: 2);
            await TestData.AddPurchaseAsync(db, thisYear, bea);
            await TestData.AddPurchaseAsync(db, thisYear, ash);
            await TestData.AddPurchaseAsync(db, lastYear, ash);
        }
        var client = app.ClientFor(UserId);

        Assert.Equal(["Ash | £30.00 | £10.00 on 1 present | + £20.00 cash", "Bea | £30.00 | £30.00 on 3 presents | No cash"],
            Cards(await client.GetAsync("/")));
        Assert.Equal(["Ash | £10.00 | £10.00 on 1 present | No cash", "Bea | £10.00 | £0.00 on 0 presents | + £10.00 cash"],
            Cards(await client.GetAsync($"/{ThisYear - 1}")));
    }

    [DatabaseFact]
    public async Task Any_year_can_be_shown_and_the_picker_lists_the_years_with_a_Christmas()
    {
        await using (var db = Db()) { await TestData.AddChristmasAsync(db, 2019); await TestData.AddChristmasAsync(db, 2021); }
        var client = app.ClientFor(UserId);

        var page = await client.GetAsync("/2015");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Christmas 2015", page.Text);
        Assert.Contains("No presents for Christmas 2015 yet.", page.Text);
        Assert.Contains("href=\"/2014\"", page.Html);
        Assert.Contains("href=\"/2016\"", page.Html);
        Assert.Equal([$"{ThisYear} (this year)", "2021", "2019", "2015"], Years(page));
    }

    [DatabaseTheory]
    [InlineData("/1999")]
    [InlineData("/3000")]
    [InlineData("/3000/Edit")]
    [InlineData("/2015/Delete")]   // no Christmas that year
    public async Task Years_out_of_range_or_without_a_Christmas_arent_found(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync(url)).Status);

    [DatabaseFact]
    public async Task A_budget_can_be_set_for_a_year_without_a_Christmas_changed_and_cleared()
    {
        var client = app.ClientFor(UserId);

        var saved = await client.PostFormAsync("/2024/Edit", await client.GetAsync("/2024/Edit"), new() { ["Christmas.Budget"] = "375" });
        Assert.Equal((HttpStatusCode.Redirect, "/2024"), (saved.Status, saved.Location));
        Assert.Contains("Budget £375.00 each", (await client.GetAsync("/2024")).Text);

        await client.PostFormAsync("/2024/Edit", await client.GetAsync("/2024/Edit"), new() { ["Christmas.Budget"] = "425.50" });
        await using (var db = Db()) Assert.Equal(425.50m, (await db.Christmases.SingleAsync()).Budget);

        await client.PostFormAsync("/2024/Edit", await client.GetAsync("/2024/Edit"), new() { ["Christmas.Budget"] = "" });
        await using (var db = Db()) Assert.Null((await db.Christmases.SingleAsync()).Budget);
        Assert.DoesNotContain("data-budget", (await client.GetAsync("/2024")).Html);
    }

    [DatabaseTheory]
    [InlineData("0", "The budget must be between £0.01 and £99,999,999.99.")]
    [InlineData("10.001", "The budget is in pounds and pence: at most two decimal places.")]
    public async Task A_budget_that_cant_be_right_is_refused(string budget, string error)
    {
        var client = app.ClientFor(UserId);

        var page = await client.PostFormAsync("/2024/Edit", await client.GetAsync("/2024/Edit"), new() { ["Christmas.Budget"] = budget });

        Assert.Contains(error, page.Errors);
        await using var db = Db();
        Assert.False(await db.Christmases.AnyAsync());
    }

    [DatabaseFact]
    public async Task A_stale_budget_edit_is_refused()
    {
        await using (var db = Db()) await TestData.AddChristmasAsync(db, 2024, 100m);
        var client = app.ClientFor(UserId);
        var (tab1, tab2) = (await client.GetAsync("/2024/Edit"), await client.GetAsync("/2024/Edit"));

        await client.PostFormAsync("/2024/Edit", tab1, new() { ["Christmas.Budget"] = "200" });
        var stale = await client.PostFormAsync("/2024/Edit", tab2, new() { ["Christmas.Budget"] = "300" });

        Assert.Contains("Someone changed this Christmas", stale.Errors);
        await using var check = Db();
        Assert.Equal(200m, (await check.Christmases.SingleAsync()).Budget);
    }

    [DatabaseFact]
    public async Task Deleting_a_Christmas_says_how_many_presents_go_with_it_and_deletes_them()
    {
        await using (var db = Db())
        {
            var child = await TestData.AddChildAsync(db);
            var christmas = await TestData.AddChristmasAsync(db, 2024);
            await TestData.AddPurchaseAsync(db, christmas, child);
            await TestData.AddPurchaseAsync(db, christmas, child);
            await TestData.AddPurchaseAsync(db, await TestData.AddChristmasAsync(db, 2025), child);
        }
        var client = app.ClientFor(UserId);

        Assert.Contains("href=\"/2024/Delete\"", (await client.GetAsync("/2024/Edit")).Html);
        var page = await client.GetAsync("/2024/Delete");
        Assert.Contains("and its <strong>2 presents</strong>", page.Html);
        var deleted = await client.PostFormAsync("/2024/Delete", page);

        Assert.Equal((HttpStatusCode.Redirect, "/2024"), (deleted.Status, deleted.Location));
        await using var check = Db();
        Assert.Equal([2025], await check.Christmases.Select(c => c.Year).ToListAsync());
        Assert.Equal(1, await check.Purchases.CountAsync());
    }

    [DatabaseFact]
    public async Task A_year_without_a_Christmas_has_nothing_to_delete() =>
        Assert.DoesNotContain("/Delete", (await app.ClientFor(UserId).GetAsync("/2024/Edit")).Html);

    [DatabaseFact]
    public async Task Another_users_Christmas_isnt_seen()
    {
        await using (var other = TestDatabase.As(NewUser())) await TestData.AddChristmasAsync(other, 2024, 999m);
        var client = app.ClientFor(UserId);

        Assert.DoesNotContain("data-budget", (await client.GetAsync("/2024")).Html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/2024/Delete")).Status);
        Assert.DoesNotContain("2024", Years(await client.GetAsync("/")));
    }
}
