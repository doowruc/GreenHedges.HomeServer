using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

public class PeoplePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task A_person_can_be_added_with_tidied_spaces_and_appears_on_the_dashboard()
    {
        var client = app.ClientFor(UserId);
        var saved = await client.PostFormAsync("/People/Create", await client.GetAsync("/People/Create"), new() { ["Person.Name"] = "  Alex   Example " });

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        await using (var db = Db()) Assert.Equal("Alex Example", (await db.People.SingleAsync()).Name);
        Assert.Contains("Alex Example", (await client.GetAsync("/")).Text);
        Assert.Contains("Alex Example", (await client.GetAsync("/People")).Text);
    }

    [DatabaseFact]
    public async Task Names_are_unique_ignoring_case()
    {
        await using (var db = Db()) await TestData.AddPersonAsync(db, "Alex");

        var client = app.ClientFor(UserId);
        var page = await client.PostFormAsync("/People/Create", await client.GetAsync("/People/Create"), new() { ["Person.Name"] = "ALEX" });

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("A person with this name already exists.", page.Errors);
    }

    [DatabaseFact]
    public async Task A_stale_edit_is_refused_rather_than_overwriting_a_newer_one()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddPersonAsync(db)).Id;
        var client = app.ClientFor(UserId);
        var url = $"/People/Edit/{id}";
        var (tab1, tab2) = (await client.GetAsync(url), await client.GetAsync(url));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync(url, tab1, new() { ["Person.Name"] = "First" })).Status);
        var stale = await client.PostFormAsync(url, tab2, new() { ["Person.Name"] = "Second" });

        Assert.Contains("Someone changed this person", stale.Errors);
        await using var check = Db();
        Assert.Equal("First", (await check.People.SingleAsync()).Name);
    }

    [DatabaseFact]
    public async Task A_person_with_payslips_cant_be_deleted()
    {
        int id;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            id = person.Id;
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m));
        }

        var client = app.ClientFor(UserId);
        var page = await client.GetAsync($"/People/Delete/{id}");
        Assert.Contains("can't be deleted while they have payslips", page.Text);
        await client.PostFormAsync($"/People/Delete/{id}", page);

        await using var check = Db();
        Assert.True(await check.People.AnyAsync());
    }

    [DatabaseFact]
    public async Task A_person_without_payslips_is_deleted_with_their_employers_and_pay_elements()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddPersonAsync(db)).Id;

        var client = app.ClientFor(UserId);
        var deleted = await client.PostFormAsync($"/People/Delete/{id}", await client.GetAsync($"/People/Delete/{id}"));

        Assert.Equal(HttpStatusCode.Redirect, deleted.Status);
        await using var check = Db();
        Assert.False(await check.People.AnyAsync());
        Assert.False(await check.Employers.IgnoreQueryFilters().AnyAsync(m => m.PersonId == id));
    }
}
