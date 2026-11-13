using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;
using static GreenHedges.HomeServer.Presents.Tests.Infrastructure.TestWorkbook;

namespace GreenHedges.HomeServer.Presents.Tests.Pages;

/// <summary>Importing the presents spreadsheet: upload, preview, import.</summary>
public class ImportPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>
    /// Laid out like the real one, with made-up presents: a year balanced to whoever had most, one with a budget, one
    /// whose cash didn't balance, and a sheet that isn't a year.
    /// </summary>
    private static byte[] Workbook() => new TestWorkbook()
        .Sheet("2023",
            R(2023, 8, 19, "Tech Shop", "Tablet", "Bea", "=640/4"),
            R(2023, 11, 2, "Sock Shop", "Socks", "Ash", "=4.99*2"),
            R(2023, 11, 3, "Toy Shop", "Kite", "Ash", 150),
            R(2023, 12, 25, null, "Cash", "Ash", 0.02),
            R(2023, 12, 1, "Toy Shop", null, "Ash", 3))
        .Sheet("2024",
            R(2024, 12, 1, "Toy Shop", "Bike", "Ash", 180),
            R(2024, 12, 1, "Book Shop", "Atlas", "Bea", 240.50),
            R(2024, 12, 25, null, "Cash", "Ash", 70),
            R(2024, 12, 25, null, "Cash", "Bea", 9.50))
        .Sheet("2018",
            R(2018, 12, 1, "Toy Shop", "Lego", "Ash", 120),
            R(2018, 12, 1, "Toy Shop", "Doll", "Bea", 150),
            R(2018, 12, 25, null, "Cash", "Ash", 20))
        .Sheet("Notes", R(2025, 1, 1, "Shop", "Ignored", "Cal", 1))
        .Build();

    private static async Task<PageResponse> PreviewAsync(PageClient client, byte[] workbook) =>
        await client.PostFileAsync("/Import?handler=Preview", await client.GetAsync("/Import"), "upload", "Presents.xlsx", workbook);

    /// <summary>A card in the preview, as text.</summary>
    private static string Card(PageResponse page, string key) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(
            Regex.Match(page.Html, $"data-import=\"{key}\".*?(?=data-import=|<button)", RegexOptions.Singleline).Value, "<[^>]+>", " ")), @"\s+", " ").Replace(" .", ".").Replace(" ,", ",");

    [DatabaseFact]
    public async Task The_preview_shows_each_year_with_its_cash_checked_and_saves_nothing()
    {
        var page = await PreviewAsync(app.ClientFor(UserId), Workbook());

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("", page.Errors);
        Assert.Contains("Children to add: Ash, Bea.", Card(page, "all"));   // in the order the oldest sheets name them
        var y2023 = Card(page, "2023");
        Assert.Contains("3 presents to import, £319.98 in all.", y2023);
        Assert.Contains("The cash worked out here matches the spreadsheet's.", y2023);
        Assert.Contains("Row 6 is skipped: it has no item.", y2023);
        Assert.Contains("Ash 2 £159.98 £0.02 £0.02 £160.00 Bea 1 £160.00 £0.00 – £160.00", y2023);
        var y2024 = Card(page, "2024");
        Assert.Contains("A budget of £250.00 each, from the cash rows.", y2024);
        Assert.Contains("Ash 1 £180.00 £70.00 £70.00 £250.00", y2024);
        var y2018 = Card(page, "2018");
        Assert.Contains("differs from the spreadsheet's", y2018);
        Assert.Contains("Ash 1 £120.00 £30.00 £20.00 £150.00", y2018);
        Assert.DoesNotContain("Cal", page.Text);
        Assert.DoesNotContain("disabled", Regex.Match(page.Html, "<button[^>]*>Import</button>").Value);

        await using var db = Db();
        Assert.False(await db.Children.AnyAsync());
        Assert.False(await db.Christmases.AnyAsync());
    }

    [DatabaseFact]
    public async Task Importing_adds_the_children_years_and_presents_and_the_cash_comes_out_as_the_sheet()
    {
        var client = app.ClientFor(UserId);

        var imported = await client.PostFormAsync("/Import?handler=Import", await PreviewAsync(client, Workbook()));

        Assert.Equal((HttpStatusCode.Redirect, "/History"), (imported.Status, imported.Location));
        Assert.Contains("Imported 7 presents in 3 years, and added 2 children.", (await client.GetAsync("/History")).Text);
        await using (var db = Db())
        {
            Assert.Equal([("Ash", 1), ("Bea", 2)], await db.Children.OrderBy(c => c.DisplayOrder).Select(c => ValueTuple.Create(c.Name, c.DisplayOrder)).ToListAsync());
            Assert.Equal([(2018, null), (2023, null), (2024, 250m)],
                (await db.Christmases.OrderBy(c => c.Year).Select(c => new { c.Year, c.Budget }).ToListAsync()).Select(c => (c.Year, c.Budget)));
            var tablet = await db.Purchases.SingleAsync(p => p.Item == "Tablet");
            Assert.Equal((160m, 1, false, false, "Tech Shop", new DateOnly(2023, 8, 19)),
                (tablet.Price, tablet.Quantity, tablet.SplitWithMum, tablet.SplitWithBirthday, tablet.Shop, tablet.Date));
            Assert.False(await db.Purchases.AnyAsync(p => p.Item == "Cash"));
        }
        Assert.Equal(["Ash | £250.00 | £180.00 on 1 present | + £70.00 cash", "Bea | £250.00 | £240.50 on 1 present | + £9.50 cash"],
            ChristmasPagesTests.Cards(await client.GetAsync("/2024")));
    }

    [DatabaseFact]
    public async Task Years_already_here_are_left_alone_and_names_match_children_here()
    {
        await using (var db = Db())
        {
            var bea = await TestData.AddChildAsync(db, "bea", 1);
            await TestData.AddPurchaseAsync(db, await TestData.AddChristmasAsync(db, 2024), bea, "Kept", 5m);
        }
        var client = app.ClientFor(UserId);

        var preview = await PreviewAsync(client, Workbook());
        Assert.Contains("Already here: left as it is.", Card(preview, "2024"));
        Assert.Contains("A child to add: Ash.", Card(preview, "all"));
        await client.PostFormAsync("/Import?handler=Import", preview);

        await using var check = Db();
        Assert.Equal(["bea", "Ash"], await check.Children.OrderBy(c => c.DisplayOrder).Select(c => c.Name).ToListAsync());
        Assert.Equal(["Kept"], await check.Purchases.Where(p => p.Christmas!.Year == 2024).Select(p => p.Item).ToListAsync());
        Assert.Equal(2, await check.Purchases.CountAsync(p => p.Christmas!.Year == 2023 && p.Child!.Name == "Ash"));
    }

    [DatabaseFact]
    public async Task With_every_year_already_here_there_is_nothing_to_import()
    {
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Import?handler=Import", await PreviewAsync(client, Workbook()));

        var preview = await PreviewAsync(client, Workbook());
        Assert.Contains("disabled", Regex.Match(preview.Html, "<button[^>]*>Import</button>").Value);
        var again = await client.PostFormAsync("/Import?handler=Import", preview);

        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Contains("There's nothing new to import", again.Errors);
    }

    [DatabaseFact]
    public async Task A_file_that_isnt_a_workbook_or_has_no_years_is_turned_away()
    {
        var client = app.ClientFor(UserId);

        Assert.Contains("That isn't a spreadsheet this can read", (await PreviewAsync(client, "not a workbook"u8.ToArray())).Errors);
        Assert.Contains("No years found", (await PreviewAsync(client, new TestWorkbook().Sheet("Sheet1").Build())).Errors);
        Assert.Contains("Choose a spreadsheet",
            (await client.PostFormAsync("/Import?handler=Preview", await client.GetAsync("/Import"))).Errors);
    }

    [DatabaseFact]
    public async Task Another_users_children_and_years_dont_count()
    {
        await using (var other = TestDatabase.As(NewUser()))
            await TestData.AddPurchaseAsync(other, await TestData.AddChristmasAsync(other, 2023), await TestData.AddChildAsync(other, "Ash"));

        var preview = await PreviewAsync(app.ClientFor(UserId), Workbook());

        Assert.Contains("Children to add: Ash, Bea.", Card(preview, "all"));
        Assert.Contains("3 presents to import", Card(preview, "2023"));
    }
}
