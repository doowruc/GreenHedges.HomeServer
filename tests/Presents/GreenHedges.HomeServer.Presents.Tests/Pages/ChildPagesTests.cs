using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Presents.Tests.Pages;

/// <summary>The Children pages: adding, renaming, reordering and deleting.</summary>
public class ChildPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private async Task<PageResponse> AddAsync(PageClient client, string name) =>
        await client.PostFormAsync("/Children/Create", await client.GetAsync("/Children/Create"), new() { ["Child.Name"] = name });

    [DatabaseFact]
    public async Task Children_are_added_after_the_others_with_tidied_spaces()
    {
        var client = app.ClientFor(UserId);
        var saved = await AddAsync(client, "  Ash   Example ");
        await AddAsync(client, "Bea");

        Assert.Equal((HttpStatusCode.Redirect, "/Children"), (saved.Status, saved.Location));
        Assert.Equal(["Ash Example", "Bea"], (await client.GetAsync("/Children")).RowNames);
        await using var db = Db();
        Assert.Equal([1, 2], await db.Children.OrderBy(c => c.Name).Select(c => c.DisplayOrder).ToListAsync());
    }

    [DatabaseFact]
    public async Task Names_are_unique_ignoring_case()
    {
        await using (var db = Db()) await TestData.AddChildAsync(db, "Ash");

        var page = await AddAsync(app.ClientFor(UserId), "ASH");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("A child with this name already exists.", page.Errors);
    }

    [DatabaseFact]
    public async Task Children_move_up_and_down_and_stay_at_the_ends()
    {
        int ash, cal;
        await using (var db = Db())
        {
            ash = (await TestData.AddChildAsync(db, "Ash", 1)).Id;
            await TestData.AddChildAsync(db, "Bea", 2);
            cal = (await TestData.AddChildAsync(db, "Cal", 5)).Id;   // a gap, as a delete leaves
        }
        var client = app.ClientFor(UserId);
        var page = await client.GetAsync("/Children");

        await client.PostFormAsync($"/Children?handler=Up&id={cal}", page);
        Assert.Equal(["Ash", "Cal", "Bea"], (await client.GetAsync("/Children")).RowNames);
        await client.PostFormAsync($"/Children?handler=Up&id={ash}", page);    // already first
        await client.PostFormAsync($"/Children?handler=Down&id={ash}", page);
        Assert.Equal(["Cal", "Ash", "Bea"], (await client.GetAsync("/Children")).RowNames);
        await using var check = Db();
        Assert.Equal([1, 2, 3], await check.Children.OrderBy(c => c.DisplayOrder).Select(c => c.DisplayOrder).ToListAsync());
    }

    [DatabaseFact]
    public async Task The_first_child_cant_move_up_or_the_last_down()
    {
        await using (var db = Db()) { await TestData.AddChildAsync(db, "Ash", 1); await TestData.AddChildAsync(db, "Bea", 2); }

        var html = (await app.ClientFor(UserId).GetAsync("/Children")).Html;

        Assert.Matches("title=\"Move up\" disabled=\"disabled\"", html);
        Assert.Matches("title=\"Move down\" disabled=\"disabled\"", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "disabled=\"disabled\"[^>]*><i class=\"bi bi-arrow-up\""));
    }

    [DatabaseFact]
    public async Task A_child_is_renamed()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddChildAsync(db, "Ash")).Id;
        var client = app.ClientFor(UserId);

        var saved = await client.PostFormAsync($"/Children/Edit/{id}", await client.GetAsync($"/Children/Edit/{id}"), new() { ["Child.Name"] = "Ashley" });

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        await using var check = Db();
        Assert.Equal("Ashley", (await check.Children.SingleAsync()).Name);
    }

    [DatabaseFact]
    public async Task A_stale_edit_is_refused_rather_than_overwriting_a_newer_one()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddChildAsync(db)).Id;
        var client = app.ClientFor(UserId);
        var url = $"/Children/Edit/{id}";
        var (tab1, tab2) = (await client.GetAsync(url), await client.GetAsync(url));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync(url, tab1, new() { ["Child.Name"] = "First" })).Status);
        var stale = await client.PostFormAsync(url, tab2, new() { ["Child.Name"] = "Second" });

        Assert.Contains("Someone changed this child", stale.Errors);
        await using var check = Db();
        Assert.Equal("First", (await check.Children.SingleAsync()).Name);
    }

    [DatabaseFact]
    public async Task A_child_with_presents_cant_be_deleted()
    {
        int id;
        await using (var db = Db())
        {
            var child = await TestData.AddChildAsync(db);
            id = child.Id;
            await TestData.AddPurchaseAsync(db, await TestData.AddChristmasAsync(db), child);
        }

        var client = app.ClientFor(UserId);
        var page = await client.GetAsync($"/Children/Delete/{id}");
        Assert.Contains("can't be deleted while they have presents (1)", page.Text);
        await client.PostFormAsync($"/Children/Delete/{id}", page);

        await using var check = Db();
        Assert.True(await check.Children.AnyAsync());
    }

    [DatabaseFact]
    public async Task A_child_without_presents_is_deleted()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddChildAsync(db)).Id;

        var client = app.ClientFor(UserId);
        var deleted = await client.PostFormAsync($"/Children/Delete/{id}", await client.GetAsync($"/Children/Delete/{id}"));

        Assert.Equal(HttpStatusCode.Redirect, deleted.Status);
        await using var check = Db();
        Assert.False(await check.Children.AnyAsync());
    }

    [DatabaseFact]
    public async Task Another_users_child_isnt_found()
    {
        int id;
        await using (var other = TestDatabase.As(NewUser())) id = (await TestData.AddChildAsync(other)).Id;

        var client = app.ClientFor(UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Children/Edit/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Children/Delete/{id}")).Status);
    }
}
