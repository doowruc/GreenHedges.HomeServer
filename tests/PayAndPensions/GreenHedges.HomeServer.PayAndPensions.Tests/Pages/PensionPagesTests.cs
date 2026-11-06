using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

public class PensionPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private async Task<(int PersonId, int ProviderId)> PersonAndProviderAsync()
    {
        await using var db = Db();
        return ((await TestData.AddPersonAsync(db)).Id, (await TestData.AddProviderAsync(db)).Id);
    }

    [DatabaseFact]
    public async Task Pensions_are_added_and_edited_for_their_person()
    {
        var (personId, providerId) = await PersonAndProviderAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/Pensions";

        var form = await client.GetAsync($"{url}/Create");
        Assert.Equal(providerId.ToString(), form.OptionValue("Example Pensions"));
        var added = await client.PostFormAsync($"{url}/Create", form, new()
        {
            ["Pension.Name"] = "Workplace pension", ["Pension.PensionProviderId"] = providerId.ToString(),
            ["Pension.PolicyNumber"] = " AB 123 ", ["Pension.Type"] = form.OptionValue("Workplace")
        });
        Assert.Equal(HttpStatusCode.Redirect, added.Status);
        Assert.Equal(url, added.Location);

        var list = await client.GetAsync(url);
        Assert.Contains("Example Pensions", list.Text);
        var edit = list.RowLink("Workplace pension", "Edit")!;
        var editForm = await client.GetAsync(edit);
        var edited = await client.PostFormAsync(edit, editForm,
            new() { ["Pension.Name"] = "My SIPP", ["Pension.Type"] = editForm.OptionValue("SIPP"), ["Pension.PolicyNumber"] = "" });
        Assert.Equal(HttpStatusCode.Redirect, edited.Status);

        Assert.Contains("<td>SIPP</td>", (await client.GetAsync(url)).Html);
        await using var check = Db();
        var pension = await check.Pensions.SingleAsync();
        Assert.Equal((personId, providerId, "My SIPP", PensionType.Sipp, null), (pension.PersonId, pension.PensionProviderId, pension.Name, pension.Type, pension.PolicyNumber));
    }

    [DatabaseFact]
    public async Task The_policy_number_is_kept_tidied()
    {
        var (personId, providerId) = await PersonAndProviderAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/Pensions/Create";

        await client.PostFormAsync(url, await client.GetAsync(url), new()
        {
            ["Pension.Name"] = "Workplace pension", ["Pension.PensionProviderId"] = providerId.ToString(), ["Pension.PolicyNumber"] = " AB  123 "
        });

        await using var check = Db();
        Assert.Equal("AB 123", await check.Pensions.Select(pn => pn.PolicyNumber).SingleAsync());
    }

    [DatabaseFact]
    public async Task A_pension_needs_one_of_the_users_providers()
    {
        var (personId, _) = await PersonAndProviderAsync();
        int othersProvider;
        await using (var other = TestDatabase.As(NewUser())) othersProvider = (await TestData.AddProviderAsync(other)).Id;
        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/Pensions/Create";

        var none = await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Pension.Name"] = "Workplace pension" });
        var others = await client.PostFormAsync(url, await client.GetAsync(url),
            new() { ["Pension.Name"] = "Workplace pension", ["Pension.PensionProviderId"] = othersProvider.ToString() });

        Assert.Contains("Choose the provider", none.Errors);
        Assert.Contains("Choose the provider", others.Errors);
        await using var check = Db();
        Assert.False(await check.Pensions.AnyAsync());
    }

    [DatabaseFact]
    public async Task Names_are_unique_per_person_only()
    {
        int alexId, samId, providerId;
        await using (var db = Db())
        {
            var provider = await TestData.AddProviderAsync(db);
            var alex = await TestData.AddPersonAsync(db, "Alex");
            var sam = await TestData.AddPersonAsync(db, "Sam");
            await TestData.AddPensionAsync(db, sam, provider);
            (alexId, samId, providerId) = (alex.Id, sam.Id, provider.Id);
        }
        var client = app.ClientFor(UserId);
        var fields = new Dictionary<string, string> { ["Pension.Name"] = "workplace PENSION", ["Pension.PensionProviderId"] = providerId.ToString() };

        var clash = await client.PostFormAsync($"/People/{samId}/Pensions/Create", await client.GetAsync($"/People/{samId}/Pensions/Create"), fields);
        var fine = await client.PostFormAsync($"/People/{alexId}/Pensions/Create", await client.GetAsync($"/People/{alexId}/Pensions/Create"), fields);

        Assert.Contains("already has a pension with this name", clash.Errors);
        Assert.Equal(HttpStatusCode.Redirect, fine.Status);
    }

    [DatabaseFact]
    public async Task A_pension_is_deleted()
    {
        int personId, pensionId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            (personId, pensionId) = (person.Id, (await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db))).Id);
        }
        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/Pensions/Delete/{pensionId}";

        var deleted = await client.PostFormAsync(url, await client.GetAsync(url));

        Assert.Equal(HttpStatusCode.Redirect, deleted.Status);
        await using var check = Db();
        Assert.False(await check.Pensions.AnyAsync());
        Assert.True(await check.PensionProviders.AnyAsync());
    }

    [DatabaseFact]
    public async Task Without_providers_the_list_says_to_add_one_first()
    {
        int personId;
        await using (var db = Db()) personId = (await TestData.AddPersonAsync(db)).Id;

        var list = await app.ClientFor(UserId).GetAsync($"/People/{personId}/Pensions");

        Assert.Contains("No pension providers yet", list.Text);
    }

    [DatabaseFact]
    public async Task Another_persons_pension_is_not_found_under_this_person()
    {
        int alexId, samsPension;
        await using (var db = Db())
        {
            var provider = await TestData.AddProviderAsync(db);
            alexId = (await TestData.AddPersonAsync(db, "Alex")).Id;
            samsPension = (await TestData.AddPensionAsync(db, await TestData.AddPersonAsync(db, "Sam"), provider)).Id;
        }

        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync($"/People/{alexId}/Pensions/Edit/{samsPension}")).Status);
    }
}
