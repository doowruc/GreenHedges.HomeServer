using System.Text.RegularExpressions;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Presents.Tests.Pages;

/// <summary>The home page: this Christmas, a card per child.</summary>
public class HomePageTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static List<string> Cards(PageResponse page) =>
        Regex.Matches(page.Html, "<div class=\"label\">([^<]*)</div>\\s*<div class=\"value\">([^<]*)</div>")
            .Select(m => $"{m.Groups[1].Value}: {m.Groups[2].Value}").ToList();

    [DatabaseFact]
    public async Task With_no_children_it_says_to_add_one()
    {
        var page = await app.ClientFor(UserId).GetAsync("/");

        Assert.Contains($"Christmas {DateTime.Today.Year}", page.Text);
        Assert.Contains("href=\"/Children/Create\"", page.Html);
    }

    [DatabaseFact]
    public async Task Each_child_has_a_card_in_order_counting_this_years_presents()
    {
        await using (var db = Db())
        {
            var bea = await TestData.AddChildAsync(db, "Bea", 2);
            var ash = await TestData.AddChildAsync(db, "Ash", 1);
            var thisYear = await TestData.AddChristmasAsync(db, DateTime.Today.Year);
            var lastYear = await TestData.AddChristmasAsync(db, DateTime.Today.Year - 1);
            await TestData.AddPurchaseAsync(db, thisYear, bea, quantity: 2);
            await TestData.AddPurchaseAsync(db, thisYear, bea);
            await TestData.AddPurchaseAsync(db, thisYear, ash);
            await TestData.AddPurchaseAsync(db, lastYear, ash);
        }

        var page = await app.ClientFor(UserId).GetAsync("/");

        Assert.Equal(["Ash: 1 present", "Bea: 3 presents"], Cards(page));
    }
}
