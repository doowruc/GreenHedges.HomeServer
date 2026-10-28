using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

public class PayElementPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task A_new_element_goes_after_the_others_with_its_kind_and_category()
    {
        int personId;
        await using (var db = Db()) personId = (await TestData.AddPersonAsync(db)).Id;   // elements up to order 50
        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/PayElements/Create";
        var form = await client.GetAsync(url);

        var saved = await client.PostFormAsync(url, form, new()
        {
            ["Element.Name"] = "Overtime", ["Element.Kind"] = form.OptionValue("Earning"), ["Element.Category"] = form.OptionValue("Overtime")
        });

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        await using var check = Db();
        var element = await check.PayElements.SingleAsync(el => el.Name == "Overtime");
        Assert.Equal((PayElementKind.Earning, PayCategory.Overtime, 60), (element.Kind, element.Category, element.SortOrder));
    }

    [DatabaseFact]
    public async Task Elements_are_listed_by_kind_then_order_with_readable_names()
    {
        int personId;
        await using (var db = Db()) personId = (await TestData.AddPersonAsync(db)).Id;

        var page = await app.ClientFor(UserId).GetAsync($"/People/{personId}/PayElements");

        // Earnings, then deductions, then the employer's contributions; within each, by order.
        Assert.Equal(["Salary", "Tax", "NI", "Pension", "Employer pension"], page.RowNames);
        Assert.Contains("<td>National Insurance</td>", page.Text);
        Assert.Contains("<td>Employer contribution</td>", page.Text);
    }

    [DatabaseFact]
    public async Task An_element_on_payslips_cant_be_deleted_but_can_be_hidden()
    {
        int personId, salaryId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            personId = person.Id;
            salaryId = person.PayElements.Single(el => el.Name == "Salary").Id;
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m));
        }
        var client = app.ClientFor(UserId);

        var delete = $"/People/{personId}/PayElements/Delete/{salaryId}";
        var page = await client.GetAsync(delete);
        Assert.Contains("can't be deleted while it's on payslips", page.Text);
        await client.PostFormAsync(delete, page);

        var edit = $"/People/{personId}/PayElements/Edit/{salaryId}";
        await client.PostFormAsync(edit, await client.GetAsync(edit), new() { ["Element.Hidden"] = "true" });

        await using var check = Db();
        Assert.True((await check.PayElements.SingleAsync(el => el.Id == salaryId)).Hidden);
    }

    [DatabaseFact]
    public async Task An_unused_element_can_be_deleted()
    {
        int personId, elementId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            (personId, elementId) = (person.Id, person.PayElements.Single(el => el.Name == "NI").Id);
        }

        var client = app.ClientFor(UserId);
        var url = $"/People/{personId}/PayElements/Delete/{elementId}";
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync(url, await client.GetAsync(url))).Status);

        await using var check = Db();
        Assert.False(await check.PayElements.AnyAsync(el => el.Id == elementId));
    }
}
