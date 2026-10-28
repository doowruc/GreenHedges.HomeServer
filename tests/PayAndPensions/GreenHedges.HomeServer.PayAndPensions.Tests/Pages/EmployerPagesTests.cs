using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

public class EmployerPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Employers_are_added_and_edited_for_their_person()
    {
        int personId;
        await using (var db = Db()) personId = (await TestData.AddPersonAsync(db)).Id;
        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/Employers";

        var added = await client.PostFormAsync($"{url}/Create", await client.GetAsync($"{url}/Create"), new() { ["Employer.Name"] = "Second Co" });
        Assert.Equal(HttpStatusCode.Redirect, added.Status);
        Assert.Equal(url, added.Location);

        var list = await client.GetAsync(url);
        var edit = list.RowLink("Second Co", "Edit")!;
        var edited = await client.PostFormAsync(edit, await client.GetAsync(edit), new() { ["Employer.Name"] = "Renamed Co" });
        Assert.Equal(HttpStatusCode.Redirect, edited.Status);

        await using var check = Db();
        Assert.Equal(["Example Ltd", "Renamed Co"], await check.Employers.Where(m => m.PersonId == personId).OrderBy(m => m.Name).Select(m => m.Name).ToListAsync());
    }

    [DatabaseFact]
    public async Task Names_are_unique_per_person_only()
    {
        int alexId, samId;
        await using (var db = Db())
        {
            alexId = (await TestData.AddPersonAsync(db, "Alex")).Id;
            samId = (await TestData.AddPersonAsync(db, "Sam")).Id;   // both work for Example Ltd
        }
        var client = app.ClientFor(UserId);

        var clash = await client.PostFormAsync($"/People/{alexId}/Employers/Create", await client.GetAsync($"/People/{alexId}/Employers/Create"),
            new() { ["Employer.Name"] = "example ltd" });

        Assert.Contains("already has an employer with this name", clash.Errors);
        await using var check = Db();
        Assert.Equal(1, await check.Employers.CountAsync(m => m.PersonId == samId));
    }

    [DatabaseFact]
    public async Task An_employer_with_payslips_cant_be_deleted()
    {
        int personId, employerId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            (personId, employerId) = (person.Id, person.Employers[0].Id);
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m));
        }

        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/Employers/Delete/{employerId}";
        var page = await client.GetAsync(url);
        Assert.Contains("can't be deleted while payslips are from them", page.Text);
        await client.PostFormAsync(url, page);

        await using var check = Db();
        Assert.True(await check.Employers.AnyAsync(m => m.Id == employerId));
    }
}
