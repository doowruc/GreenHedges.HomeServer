using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>A pension's statement types and statements.</summary>
public class StatementPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>Alex's workplace pension, started 1 January 2020, with an investment report and an annual illustration.</summary>
    private async Task<(Person Person, Pension Pension, StatementType Report, StatementType Illustration)> PensionAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db));
        await db.Pensions.Where(pn => pn.Id == pension.Id).ExecuteUpdateAsync(s => s.SetProperty(pn => pn.StartDate, new DateOnly(2020, 1, 1)));
        var report = await TestData.AddStatementTypeAsync(db, pension);
        var illustration = await TestData.AddStatementTypeAsync(db, pension, "Annual illustration", hasProjections: true);
        return (person, pension, report, illustration);
    }

    private static string Url(Person person, Pension pension, string page = "") =>
        $"/People/{person.Id}/Pensions/{pension.Id}/{page}";

    /// <summary>One statement type's table, as rows of cell text.</summary>
    private static List<List<string>> Table(PageResponse page, string type) =>
        Regex.Matches(Regex.Match(page.Html, $"data-statements=\"{Regex.Escape(type)}\".*?</table>", RegexOptions.Singleline).Value,
                "<tr data-row-menu.*?</tr>", RegexOptions.Singleline)
            .Select(row => Regex.Matches(row.Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Select(c => WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", "")).Trim()).ToList())
            .ToList();

    [DatabaseFact]
    public async Task Each_type_has_a_table_latest_first_with_the_figures_worked_out()
    {
        var (person, pension, report, illustration) = await PensionAsync();
        await using (var db = Db())
        {
            await TestData.AddStatementAsync(db, report, new DateOnly(2020, 4, 10), 1000m, "Spring 2020");      // day 100
            await TestData.AddStatementAsync(db, report, new DateOnly(2020, 7, 19), 1500m, "Summer 2020");      // day 200
            await TestData.AddStatementAsync(db, illustration, new DateOnly(2020, 6, 1), 1200m, projectedFund: 250000m, projectedAnnualIncome: 6000m);
        }

        var page = await app.ClientFor(UserId).GetAsync(Url(person, pension, "Statements"));

        var rows = Table(page, "Investment report");
        Assert.Equal(["19/07/20", "Summer 2020", "1,500.00", "+500.00", "+50.0%", "0.00", "200", "7.50", "100", "+5.00"], rows[0][..10]);
        Assert.Equal(["10/04/20", "Spring 2020", "1,000.00", "+1,000.00", "", "-500.00", "100", "10.00", "100", "+10.00"], rows[1][..10]);
        Assert.Equal(["250,000", "6,000.00", "500.00"], Table(page, "Annual illustration")[0][10..13]);
        Assert.Contains("Started 1 January 2020", page.Text);
    }

    [DatabaseFact]
    public async Task A_statement_is_added_with_its_types_latest_label_to_change_then_edited_and_deleted()
    {
        var (person, pension, report, _) = await PensionAsync();
        await using (var db = Db()) await TestData.AddStatementAsync(db, report, new DateOnly(2026, 4, 1), 1000m, "Spring 2026");
        var client = app.ClientFor(UserId);
        var url = Url(person, pension, "Statements");

        var form = await client.GetAsync($"{url}/Create?typeId={report.Id}");
        Assert.Contains("value=\"Spring 2026\"", form.Html);
        var added = await client.PostFormAsync($"{url}/Create", form,
            new() { ["Statement.Date"] = "2026-07-01", ["Statement.Label"] = " Summer   2026 ", ["Statement.Value"] = "1100.50" });
        Assert.Equal(HttpStatusCode.Redirect, added.Status);
        Assert.Equal(url, added.Location);

        int id;
        await using (var check = Db())
        {
            var statement = await check.PensionStatements.SingleAsync(st => st.Date == new DateOnly(2026, 7, 1));
            Assert.Equal((report.Id, "Summer 2026", 1100.50m), (statement.StatementTypeId, statement.Label, statement.Value));
            id = statement.Id;
        }

        var edited = await client.PostFormAsync($"{url}/Edit/{id}", await client.GetAsync($"{url}/Edit/{id}"), new() { ["Statement.Value"] = "1200" });
        Assert.Equal(HttpStatusCode.Redirect, edited.Status);
        var deleted = await client.PostFormAsync($"{url}/Delete/{id}", await client.GetAsync($"{url}/Delete/{id}"));
        Assert.Equal(HttpStatusCode.Redirect, deleted.Status);

        await using var after = Db();
        Assert.Equal([1000m], await after.PensionStatements.Select(st => st.Value).ToListAsync());
    }

    [DatabaseFact]
    public async Task Projections_are_only_for_a_type_with_them_and_a_date_only_once_per_type()
    {
        var (person, pension, report, illustration) = await PensionAsync();
        await using (var db = Db()) await TestData.AddStatementAsync(db, report, new DateOnly(2026, 4, 1), 1000m);
        var client = app.ClientFor(UserId);
        var url = Url(person, pension, "Statements/Create");

        var projected = await client.PostFormAsync(url, await client.GetAsync($"{url}?typeId={report.Id}"),
            new() { ["Statement.Date"] = "2026-07-01", ["Statement.Value"] = "1", ["Statement.ProjectedFund"] = "100000" });
        var twice = await client.PostFormAsync(url, await client.GetAsync($"{url}?typeId={report.Id}"),
            new() { ["Statement.Date"] = "2026-04-01", ["Statement.Value"] = "1" });
        var otherType = await client.PostFormAsync(url, await client.GetAsync($"{url}?typeId={illustration.Id}"),
            new() { ["Statement.Date"] = "2026-04-01", ["Statement.Value"] = "1", ["Statement.ProjectedFund"] = "100000" });

        Assert.Contains("Investment report statements have no projections", projected.Errors);
        Assert.Contains("already a Investment report statement on", twice.Errors);
        Assert.Equal(HttpStatusCode.Redirect, otherType.Status);   // the same date is fine for another type
    }

    [DatabaseFact]
    public async Task A_statement_needs_one_of_the_pensions_own_types()
    {
        var (person, pension, _, _) = await PensionAsync();
        int othersType;
        await using (var db = Db())
        {
            var sam = await TestData.AddPersonAsync(db, "Sam");
            othersType = (await TestData.AddStatementTypeAsync(db, await TestData.AddPensionAsync(db, sam, await db.PensionProviders.SingleAsync()))).Id;
        }
        var client = app.ClientFor(UserId);
        var url = Url(person, pension, "Statements/Create");

        var page = await client.PostFormAsync(url, await client.GetAsync(url),
            new() { ["Statement.StatementTypeId"] = othersType.ToString(), ["Statement.Date"] = "2026-04-01", ["Statement.Value"] = "1" });

        Assert.Contains("Choose one of this pension's statement types", page.Errors);
    }

    [DatabaseFact]
    public async Task Types_are_unique_per_pension_and_cant_be_deleted_or_lose_projections_while_in_use()
    {
        var (person, pension, report, illustration) = await PensionAsync();
        await using (var db = Db())
        {
            await TestData.AddStatementAsync(db, report, new DateOnly(2026, 4, 1), 1000m);
            await TestData.AddStatementAsync(db, illustration, new DateOnly(2026, 4, 1), 1000m, projectedFund: 1m);
        }
        var client = app.ClientFor(UserId);
        var url = Url(person, pension, "StatementTypes");

        var clash = await client.PostFormAsync($"{url}/Create", await client.GetAsync($"{url}/Create"), new() { ["Type.Name"] = "investment REPORT" });
        Assert.Contains("already has a statement type with this name", clash.Errors);

        var unproject = await client.PostFormAsync($"{url}/Edit/{illustration.Id}", await client.GetAsync($"{url}/Edit/{illustration.Id}"),
            new() { ["Type.HasProjections"] = "false" });
        Assert.Contains("Its statements have projections", unproject.Errors);

        var delete = await client.GetAsync($"{url}/Delete/{report.Id}");
        Assert.Contains("can't be deleted while it has statements (1)", delete.Text);
        await client.PostFormAsync($"{url}/Delete/{report.Id}", delete);
        await using var check = Db();
        Assert.Equal(2, await check.StatementTypes.CountAsync());
    }

    [DatabaseFact]
    public async Task Adding_a_statement_without_types_goes_to_adding_a_type()
    {
        int personId, pensionId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            (personId, pensionId) = (person.Id, (await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db))).Id);
        }

        var page = await app.ClientFor(UserId).GetAsync($"/People/{personId}/Pensions/{pensionId}/Statements/Create");

        Assert.Equal(HttpStatusCode.Redirect, page.Status);
        Assert.Equal($"/People/{personId}/Pensions/{pensionId}/StatementTypes/Create", page.Location);
    }

    [DatabaseFact]
    public async Task A_pension_or_person_with_statements_cant_be_deleted()
    {
        var (person, pension, report, _) = await PensionAsync();
        await using (var db = Db()) await TestData.AddStatementAsync(db, report, new DateOnly(2026, 4, 1), 1000m);
        var client = app.ClientFor(UserId);

        var pensionPage = await client.GetAsync($"/People/{person.Id}/Pensions/Delete/{pension.Id}");
        Assert.Contains("can't be deleted while it has statements (1)", pensionPage.Text);
        await client.PostFormAsync($"/People/{person.Id}/Pensions/Delete/{pension.Id}", pensionPage);
        var personPage = await client.GetAsync($"/People/Delete/{person.Id}");
        Assert.Contains("can't be deleted while their pensions have statements (1)", personPage.Text);
        await client.PostFormAsync($"/People/Delete/{person.Id}", personPage);

        await using var check = Db();
        Assert.True(await check.Pensions.AnyAsync());
        Assert.True(await check.People.AnyAsync());
    }

    [DatabaseFact]
    public async Task A_pension_without_statements_is_deleted_with_its_types()
    {
        var (person, pension, _, _) = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Pensions/Delete/{pension.Id}";

        await client.PostFormAsync(url, await client.GetAsync(url));

        await using var check = Db();
        Assert.False(await check.Pensions.AnyAsync());
        Assert.False(await check.StatementTypes.IgnoreQueryFilters().AnyAsync(t => t.PensionId == pension.Id));
    }

    [DatabaseFact]
    public async Task Another_persons_pension_isnt_found_under_this_person()
    {
        var (_, pension, report, _) = await PensionAsync();
        int samId, statementId;
        await using (var db = Db())
        {
            samId = (await TestData.AddPersonAsync(db, "Sam")).Id;
            statementId = (await TestData.AddStatementAsync(db, report, new DateOnly(2026, 4, 1), 1m)).Id;
        }
        var client = app.ClientFor(UserId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/People/{samId}/Pensions/{pension.Id}/Statements")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/People/{samId}/Pensions/{pension.Id}/Statements/Edit/{statementId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync($"/People/{samId}/Pensions/{pension.Id}/Statements")).Status);
    }

    [DatabaseFact]
    public async Task A_pension_gets_a_start_date_from_its_form()
    {
        var (person, pension, _, _) = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = $"/People/{person.Id}/Pensions/Edit/{pension.Id}";

        var form = await client.GetAsync(url);
        Assert.Contains("value=\"2020-01-01\"", form.Html);
        await client.PostFormAsync(url, form, new() { ["Pension.StartDate"] = "2015-03-16" });

        await using var check = Db();
        Assert.Equal(new DateOnly(2015, 3, 16), await check.Pensions.Select(pn => pn.StartDate).SingleAsync());
    }
}
