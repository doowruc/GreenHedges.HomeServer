using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Models;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Presents.Tests.Pages;

/// <summary>Adding presents on a Christmas's page, the list, and editing and deleting them.</summary>
public class PurchasePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly int ThisYear = DateTime.Today.Year;

    /// <summary>The presents table, as rows of cell text (without the actions).</summary>
    private static List<List<string>> Rows(PageResponse page) =>
        Regex.Matches(Regex.Match(page.Html, "data-purchases.*?</table>", RegexOptions.Singleline).Value, "<tr>.*?</tr>", RegexOptions.Singleline)
            .Select(row => Regex.Matches(row.Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Select(c => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", " ")), @"\s+", " ").Trim()).Take(6).ToList())
            .Where(cells => cells.Count > 0)
            .ToList();

    private async Task<(int Ash, int Bea)> ChildrenAsync()
    {
        await using var db = Db();
        return ((await TestData.AddChildAsync(db, "Ash", 1)).Id, (await TestData.AddChildAsync(db, "Bea", 2)).Id);
    }

    private static Dictionary<string, string> Fields(int childId, string item = "Jigsaw", string price = "12.50", string date = "",
        string shop = "", string quantity = "1", bool mum = false, bool birthday = false)
    {
        var fields = new Dictionary<string, string>
        {
            ["Purchase.ChildId"] = childId.ToString(), ["Purchase.Item"] = item, ["Purchase.Price"] = price,
            ["Purchase.Date"] = date, ["Purchase.Shop"] = shop, ["Purchase.Quantity"] = quantity
        };
        if (mum) fields["Purchase.SplitWithMum"] = "true";
        if (birthday) fields["Purchase.SplitWithBirthday"] = "true";
        return fields;
    }

    private async Task<PageResponse> AddAsync(PageClient client, string url, Dictionary<string, string> fields) =>
        await client.PostFormAsync(url, await client.GetAsync(url), fields);

    [DatabaseFact]
    public async Task The_form_starts_on_today_this_year_and_blank_in_others()
    {
        await ChildrenAsync();
        var client = app.ClientFor(UserId);

        Assert.Contains($"value=\"{DateTime.Today:yyyy-MM-dd}\"", (await client.GetAsync("/")).Html);
        Assert.DoesNotContain($"value=\"{DateTime.Today:yyyy-MM-dd}\"", (await client.GetAsync("/2020")).Html);
    }

    [DatabaseFact]
    public async Task The_first_present_makes_the_years_Christmas_and_the_form_comes_back_on_the_same_child_shop_and_date()
    {
        var (_, bea) = await ChildrenAsync();
        var client = app.ClientFor(UserId);

        var added = await AddAsync(client, "/2024", Fields(bea, "  Lego   set ", "49.99", "2024-11-30", " Toy Shop "));

        Assert.Equal(HttpStatusCode.Redirect, added.Status);
        Assert.Equal($"/2024?child={bea}&shop=Toy%20Shop&date=2024-11-30", added.Location);
        var form = await client.GetAsync(added.Location!);
        Assert.Contains("Added Lego set for Bea.", form.Text);
        Assert.Matches($"<option value=\"{bea}\" selected=\"selected\">|<option selected=\"selected\" value=\"{bea}\">", form.Html);
        Assert.Matches("value=\"Toy Shop\"", form.Html);
        Assert.Contains("value=\"2024-11-30\"", form.Html);
        Assert.Equal([["30 Nov", "Bea", "Lego set", "Toy Shop", "£49.99", "£49.99"]], Rows(form));

        await using var db = Db();
        var christmas = await db.Christmases.Include(c => c.Purchases).SingleAsync();
        Assert.Equal((2024, null), (christmas.Year, christmas.Budget));
        Assert.Equal(("Lego set", "Toy Shop", 49.99m, 1), (christmas.Purchases[0].Item, christmas.Purchases[0].Shop, christmas.Purchases[0].Price, christmas.Purchases[0].Quantity));
    }

    [DatabaseFact]
    public async Task Splits_and_quantities_are_kept_and_the_list_shows_what_it_counts_as()
    {
        var (ash, bea) = await ChildrenAsync();
        var client = app.ClientFor(UserId);

        await AddAsync(client, "/2023", Fields(bea, "Tablet", "780", "2023-08-19", mum: true, birthday: true));
        await AddAsync(client, "/2023", Fields(ash, "Phone", "640", "2023-10-17", birthday: true));
        await AddAsync(client, "/2023", Fields(ash, "Socks", "9.99", "2023-12-01", quantity: "2"));
        await AddAsync(client, "/2023", Fields(bea, "Chocolate", "3", ""));

        var page = await client.GetAsync("/2023");
        Assert.Equal([
            ["1 Dec", "Ash", "Socks", "", "2 × £9.99", "£19.98"],
            ["17 Oct", "Ash", "Phone", "", "£640.00", "£320.00"],
            ["19 Aug", "Bea", "Tablet", "", "£780.00", "£195.00"],
            ["–", "Bea", "Chocolate", "", "£3.00", "£3.00"]], Rows(page));
        Assert.Contains("title=\"Split with Mum and birthday\"", page.Html);
        Assert.Contains("title=\"Split with birthday\"", page.Html);

        await using var db = Db();
        var tablet = await db.Purchases.SingleAsync(p => p.Item == "Tablet");
        Assert.Equal((780m, true, true), (tablet.Price, tablet.SplitWithMum, tablet.SplitWithBirthday));
    }

    [DatabaseTheory]
    [InlineData("Purchase.Item", "", "Enter what it is.")]
    [InlineData("Purchase.Item", "   ", "Enter what it is.")]
    [InlineData("Purchase.Price", "", "Enter the price.")]
    [InlineData("Purchase.Price", "0", "The price must be between £0.01 and £99,999,999.99.")]
    [InlineData("Purchase.Price", "1.234", "The price is in pounds and pence: at most two decimal places.")]
    [InlineData("Purchase.Quantity", "0", "The quantity must be between 1 and 999.")]
    [InlineData("Purchase.Date", "2999-01-01", "The date can't be in the future.")]
    [InlineData("Purchase.ChildId", "", "Choose who it's for.")]
    public async Task A_present_that_cant_be_right_is_refused(string field, string value, string error)
    {
        var (ash, _) = await ChildrenAsync();
        var fields = Fields(ash);
        fields[field] = value;

        var page = await AddAsync(app.ClientFor(UserId), "/2024", fields);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(error, page.Errors);
        await using var db = Db();
        Assert.False(await db.Purchases.AnyAsync());
        Assert.False(await db.Christmases.AnyAsync());
    }

    [DatabaseFact]
    public async Task Another_users_child_cant_be_given_a_present()
    {
        await ChildrenAsync();
        int theirs;
        await using (var other = TestDatabase.As(NewUser())) theirs = (await TestData.AddChildAsync(other, "Cal")).Id;

        var page = await AddAsync(app.ClientFor(UserId), "/2024", Fields(theirs));

        Assert.Contains("Choose who it's for.", page.Errors);
        await using var db = Db();
        Assert.False(await db.Purchases.IgnoreQueryFilters().AnyAsync(p => p.ChildId == theirs));
    }

    [DatabaseFact]
    public async Task Shops_used_before_are_suggested()
    {
        var (ash, _) = await ChildrenAsync();
        await using (var db = Db())
        {
            var christmas = await TestData.AddChristmasAsync(db, 2022);
            await TestData.AddPurchaseAsync(db, christmas, await db.Children.FirstAsync(c => c.Id == ash), shop: "Toy Shop");
            await TestData.AddPurchaseAsync(db, christmas, await db.Children.FirstAsync(c => c.Id == ash), shop: "Book Shop");
        }

        var html = (await app.ClientFor(UserId).GetAsync("/")).Html;

        Assert.Matches("<datalist id=\"shops\">\\s*<option value=\"Book Shop\"></option>\\s*<option value=\"Toy Shop\"></option>", html);
    }

    [DatabaseFact]
    public async Task A_present_is_edited_and_can_move_to_another_Christmas()
    {
        var (ash, bea) = await ChildrenAsync();
        int id;
        await using (var db = Db())
        {
            var christmas = await TestData.AddChristmasAsync(db, 2024);
            id = (await TestData.AddPurchaseAsync(db, christmas, await db.Children.FirstAsync(c => c.Id == ash), "Jigsaw", 10m)).Id;
        }
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync($"/Purchases/Edit/{id}");
        Assert.Contains("name=\"Purchase.Year\"", form.Html);
        Assert.Contains("href=\"/2024\"", form.Html);   // Cancel

        var fields = Fields(bea, "Big jigsaw", "20", "2025-01-02", mum: true);
        fields["Purchase.Year"] = "2025";
        var saved = await client.PostFormAsync($"/Purchases/Edit/{id}", form, fields);

        Assert.Equal((HttpStatusCode.Redirect, "/2025"), (saved.Status, saved.Location));
        Assert.Equal([["2 Jan", "Bea", "Big jigsaw", "", "£20.00", "£10.00"]], Rows(await client.GetAsync("/2025")));
        Assert.Empty(Rows(await client.GetAsync("/2024")));
    }

    [DatabaseFact]
    public async Task A_stale_edit_is_refused_rather_than_overwriting_a_newer_one()
    {
        var (ash, _) = await ChildrenAsync();
        int id;
        await using (var db = Db())
            id = (await TestData.AddPurchaseAsync(db, await TestData.AddChristmasAsync(db, 2024), await db.Children.FirstAsync(c => c.Id == ash))).Id;
        var client = app.ClientFor(UserId);
        var url = $"/Purchases/Edit/{id}";
        var (tab1, tab2) = (await client.GetAsync(url), await client.GetAsync(url));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync(url, tab1, new() { ["Purchase.Item"] = "First" })).Status);
        var stale = await client.PostFormAsync(url, tab2, new() { ["Purchase.Item"] = "Second" });

        Assert.Contains("Someone changed this present", stale.Errors);
        await using var check = Db();
        Assert.Equal("First", (await check.Purchases.SingleAsync()).Item);
    }

    [DatabaseFact]
    public async Task A_present_is_deleted_after_asking()
    {
        var (ash, _) = await ChildrenAsync();
        int id;
        await using (var db = Db())
            id = (await TestData.AddPurchaseAsync(db, await TestData.AddChristmasAsync(db, 2024), await db.Children.FirstAsync(c => c.Id == ash),
                "Phone", 600m, splitWithMum: true, splitWithBirthday: true)).Id;
        var client = app.ClientFor(UserId);

        var page = await client.GetAsync($"/Purchases/Delete/{id}");
        Assert.Contains("Phone for Ash (Christmas 2024, counting £150.00)", page.Text);
        var deleted = await client.PostFormAsync($"/Purchases/Delete/{id}", page);

        Assert.Equal((HttpStatusCode.Redirect, "/2024"), (deleted.Status, deleted.Location));
        await using var check = Db();
        Assert.False(await check.Purchases.AnyAsync());
        Assert.True(await check.Christmases.AnyAsync());   // the year stays
    }

    [DatabaseFact]
    public async Task Another_users_present_isnt_found()
    {
        int id;
        await using (var other = TestDatabase.As(NewUser()))
            id = (await TestData.AddPurchaseAsync(other, await TestData.AddChristmasAsync(other), await TestData.AddChildAsync(other))).Id;
        var client = app.ClientFor(UserId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Purchases/Edit/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Purchases/Delete/{id}")).Status);
        await using var check = TestDatabase.As(null);
        Assert.True(await check.Purchases.IgnoreQueryFilters().AnyAsync(p => p.Id == id));
    }
}
