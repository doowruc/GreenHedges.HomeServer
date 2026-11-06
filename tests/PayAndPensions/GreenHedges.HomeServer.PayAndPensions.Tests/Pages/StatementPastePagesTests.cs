using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>Pasting a statement type's history in: made-up rows, tab-separated as Excel copies them.</summary>
public class StatementPastePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly string Illustration = string.Join("\r\n",
        "Date\tValue\tChange\tRetirement\tAnnual\tMonthly",
        "15/03/2015\t0\t\t\t\t",
        "01/09/2015\t2,000\t2000\t150,000\t4,500\t375",
        "01/09/2016\t2,500\t500\t160,000\t4,800\t400",
        "someday\t3,000\t500\t\t\t");

    private async Task<(int PersonId, int PensionId, int TypeId)> PensionAsync(DateOnly? startDate = null)
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db));
        if (startDate is not null) await db.Pensions.Where(pn => pn.Id == pension.Id).ExecuteUpdateAsync(s => s.SetProperty(pn => pn.StartDate, startDate));
        var type = await TestData.AddStatementTypeAsync(db, pension, "Annual illustration", hasProjections: true);
        return (person.Id, pension.Id, type.Id);
    }

    private static string Url((int PersonId, int PensionId, int TypeId) ids) =>
        $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Statements/Paste/{ids.TypeId}";

    private async Task<PageResponse> PreviewAsync(PageClient client, string url, string text) =>
        await client.PostFormAsync($"{url}?handler=Preview", await client.GetAsync(url), new() { ["Text"] = text });

    [DatabaseFact]
    public async Task The_preview_shows_what_would_be_imported_and_saves_nothing()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);

        var preview = await PreviewAsync(client, Url(ids), Illustration);

        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Contains("<strong>2</strong> statements to import: 01/09/2015 to 01/09/2016", preview.Html);
        Assert.Contains("Columns ignored: Change, Monthly.", preview.Text);
        Assert.Contains("Row 5: \"someday\" isn't a date.", preview.Text);
        Assert.Contains("Set Workplace pension's start date to 15 March 2015", preview.Text);
        Assert.Contains("+500.00", preview.Text);
        Assert.Contains("+25.0%", preview.Text);
        await using var check = Db();
        Assert.False(await check.PensionStatements.AnyAsync());
    }

    [DatabaseFact]
    public async Task Importing_adds_the_statements_and_the_start_date()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids);

        var imported = await client.PostFormAsync($"{url}?handler=Import", await PreviewAsync(client, url, Illustration));

        Assert.Equal(HttpStatusCode.Redirect, imported.Status);
        Assert.Equal($"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Statements", imported.Location);
        await using var check = Db();
        Assert.Equal([(new DateOnly(2015, 9, 1), 2000m, 150000m, 4500m), (new DateOnly(2016, 9, 1), 2500m, 160000m, 4800m)],
            (await check.PensionStatements.OrderBy(st => st.Date).ToListAsync())
                .Select(st => (st.Date, st.Value, st.ProjectedFund ?? 0, st.ProjectedAnnualIncome ?? 0)));
        Assert.Equal(new DateOnly(2015, 3, 15), await check.Pensions.Select(pn => pn.StartDate).SingleAsync());
    }

    [DatabaseFact]
    public async Task Dates_already_there_are_left_alone_and_the_start_date_can_be_left_unset()
    {
        var ids = await PensionAsync();
        await using (var db = Db())
            await TestData.AddStatementAsync(db, await db.StatementTypes.SingleAsync(), new DateOnly(2015, 9, 1), 1999m);
        var client = app.ClientFor(UserId);
        var url = Url(ids);

        var preview = await PreviewAsync(client, url, Illustration);
        Assert.Contains("1 dates Annual illustration already has", preview.Text);
        await client.PostFormAsync($"{url}?handler=Import", preview, new() { ["SetStartDate"] = "false" });

        await using var check = Db();
        Assert.Equal([1999m, 2500m], await check.PensionStatements.OrderBy(st => st.Date).Select(st => st.Value).ToListAsync());
        Assert.Null(await check.Pensions.Select(pn => pn.StartDate).SingleAsync());
    }

    [DatabaseFact]
    public async Task A_pension_with_a_start_date_keeps_it()
    {
        var ids = await PensionAsync(new DateOnly(2014, 1, 1));
        var client = app.ClientFor(UserId);
        var url = Url(ids);

        var preview = await PreviewAsync(client, url, Illustration);
        Assert.DoesNotContain("start date to", preview.Text);
        await client.PostFormAsync($"{url}?handler=Import", preview);

        await using var check = Db();
        Assert.Equal(new DateOnly(2014, 1, 1), await check.Pensions.Select(pn => pn.StartDate).SingleAsync());
    }

    [DatabaseFact]
    public async Task A_paste_without_headings_is_refused()
    {
        var ids = await PensionAsync();

        var preview = await PreviewAsync(app.ClientFor(UserId), Url(ids), "01/09/2015\t2000");

        Assert.Contains("The first row must be the headings, with Date and Value", preview.Errors);
    }

    [DatabaseFact]
    public async Task Another_pensions_type_is_not_found()
    {
        var ids = await PensionAsync();
        int samsType;
        await using (var db = Db())
        {
            var sam = await TestData.AddPersonAsync(db, "Sam");
            samsType = (await TestData.AddStatementTypeAsync(db, await TestData.AddPensionAsync(db, sam, await db.PensionProviders.SingleAsync()))).Id;
        }

        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync(Url(ids with { TypeId = samsType }))).Status);
    }
}
