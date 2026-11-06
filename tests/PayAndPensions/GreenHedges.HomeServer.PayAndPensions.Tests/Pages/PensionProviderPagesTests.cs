using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

public class PensionProviderPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private const string Url = "/PensionProviders";

    [DatabaseFact]
    public async Task Providers_are_added_and_edited_and_a_website_gets_its_https()
    {
        var client = app.ClientFor(UserId);

        var added = await client.PostFormAsync($"{Url}/Create", await client.GetAsync($"{Url}/Create"),
            new() { ["Provider.Name"] = "  Example   Pensions ", ["Provider.Website"] = "www.example.com/sign-in" });
        Assert.Equal(HttpStatusCode.Redirect, added.Status);
        Assert.Equal(Url, added.Location);

        var list = await client.GetAsync(Url);
        Assert.Contains("href=\"https://www.example.com/sign-in\"", list.Html);
        var edit = list.RowLink("Example Pensions", "Edit")!;
        var edited = await client.PostFormAsync(edit, await client.GetAsync(edit),
            new() { ["Provider.Name"] = "Renamed Pensions", ["Provider.Website"] = "" });
        Assert.Equal(HttpStatusCode.Redirect, edited.Status);

        await using var check = Db();
        var provider = await check.PensionProviders.SingleAsync();
        Assert.Equal(("Renamed Pensions", null, UserId), (provider.Name, provider.Website, provider.UserId));
    }

    [DatabaseTheory]
    [InlineData("not a website")]
    [InlineData("ftp://example.com")]
    [InlineData("https://localhost")]
    public async Task A_website_must_be_a_web_address(string website)
    {
        var client = app.ClientFor(UserId);

        var page = await client.PostFormAsync($"{Url}/Create", await client.GetAsync($"{Url}/Create"),
            new() { ["Provider.Name"] = "Example Pensions", ["Provider.Website"] = website });

        Assert.Contains("Enter a website address", page.Errors);
        await using var check = Db();
        Assert.False(await check.PensionProviders.AnyAsync());
    }

    [DatabaseFact]
    public async Task Names_are_unique_per_user()
    {
        await using (var db = Db()) await TestData.AddProviderAsync(db);
        await using (var other = TestDatabase.As(NewUser())) await TestData.AddProviderAsync(other);   // no clash
        var client = app.ClientFor(UserId);

        var clash = await client.PostFormAsync($"{Url}/Create", await client.GetAsync($"{Url}/Create"),
            new() { ["Provider.Name"] = "example pensions" });

        Assert.Contains("already a provider with this name", clash.Errors);
    }

    [DatabaseFact]
    public async Task The_list_shows_everyones_pensions_with_each_provider()
    {
        await using (var db = Db())
        {
            var provider = await TestData.AddProviderAsync(db);
            await TestData.AddPensionAsync(db, await TestData.AddPersonAsync(db, "Sam"), provider);
            await TestData.AddPensionAsync(db, await TestData.AddPersonAsync(db, "Alex"), provider, "Old job's pension");
        }

        var list = await app.ClientFor(UserId).GetAsync(Url);

        Assert.Contains("Alex: Old job's pension", list.Text);
        Assert.Contains("Sam: Workplace pension", list.Text);
        Assert.True(list.Text.IndexOf("Alex:", StringComparison.Ordinal) < list.Text.IndexOf("Sam:", StringComparison.Ordinal));
    }

    [DatabaseFact]
    public async Task A_provider_with_pensions_cant_be_deleted_but_one_without_can()
    {
        int used, unused;
        await using (var db = Db())
        {
            var provider = await TestData.AddProviderAsync(db);
            await TestData.AddPensionAsync(db, await TestData.AddPersonAsync(db), provider);
            (used, unused) = (provider.Id, (await TestData.AddProviderAsync(db, "Spare Pensions")).Id);
        }
        var client = app.ClientFor(UserId);

        var page = await client.GetAsync($"{Url}/Delete/{used}");
        Assert.Contains("can't be deleted while pensions are with it (1)", page.Text);
        await client.PostFormAsync($"{Url}/Delete/{used}", page);
        var deleted = await client.PostFormAsync($"{Url}/Delete/{unused}", await client.GetAsync($"{Url}/Delete/{unused}"));
        Assert.Equal(HttpStatusCode.Redirect, deleted.Status);

        await using var check = Db();
        Assert.Equal([used], await check.PensionProviders.Select(pp => pp.Id).ToListAsync());
    }

    [DatabaseFact]
    public async Task Another_users_provider_is_not_found()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddProviderAsync(db)).Id;
        var client = app.ClientFor(NewUser());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/Edit/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/Delete/{id}")).Status);
    }
}
