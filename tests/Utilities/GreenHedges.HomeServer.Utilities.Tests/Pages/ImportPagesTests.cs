using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;
using static GreenHedges.HomeServer.Utilities.Tests.Infrastructure.TestWorkbook;

namespace GreenHedges.HomeServer.Utilities.Tests.Pages;

/// <summary>Importing the meter readings spreadsheet: upload, preview, import.</summary>
public class ImportPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>Laid out like the real sheet, with made-up readings: a meter change in each of electricity and gas.</summary>
    private static byte[] Workbook() => new TestWorkbook
        {
            Strays = { (2, 6, 1, "E"), (2, 9, 0, "G") }
        }
        .Block("Electricity", R(2021, 2, 1, 5000), R(2021, 3, 1, 5280), R(2021, 3, 21, 150), R(2021, 3, 31, 240.5))
        .Block("Gas", R(2021, 2, 1, 800), R(2021, 3, 1, 856), R(2021, 3, 21, 20.125))
        .Block("Water In", R(2020, 6, 1, 330), R(2021, 1, 1, 380))
        .Block("Water In", R(2020, 6, 1, 999))
        .Build();

    private static async Task<PageResponse> PreviewAsync(PageClient client, byte[] workbook) =>
        await client.PostFileAsync("/Import?handler=Preview", await client.GetAsync("/Import"), "upload", "Readings.xlsx", workbook);

    /// <summary>A utility's card in the preview, as text.</summary>
    private static string Card(PageResponse page, UtilityType utility) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(
            Regex.Match(page.Html, $"data-import=\"{utility}\".*?</div></div>", RegexOptions.Singleline).Value, "<[^>]+>", " ")), @"\s+", " ");

    [DatabaseFact]
    public async Task The_preview_shows_what_each_utility_would_gain_and_saves_nothing()
    {
        var page = await PreviewAsync(app.ClientFor(UserId), Workbook());

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("", page.Errors);
        var electricity = Card(page, UtilityType.Electricity);
        Assert.Contains("4 readings to import: 1 Feb 2021 to 31 Mar 2021.", electricity);
        Assert.Contains("2 meters: the first, and a replacement where the reading falls, on 21 Mar 2021.", electricity);
        Assert.Contains("Units a day match the spreadsheet's own on every row.", electricity);
        Assert.Contains("21 Mar 2021 New meter 150 150 20 7.50 5", electricity);
        Assert.Contains("3 readings to import", Card(page, UtilityType.Gas));
        var water = Card(page, UtilityType.Water);
        Assert.Contains("2 readings to import", water);
        Assert.Contains("1 meter.", water);
        Assert.Contains("Row 9 is skipped: it has a reading but no date.", water);
        Assert.Contains("Row 12 is skipped: its date isn't a date.", water);
        Assert.DoesNotContain("999", page.Text);   // the second Water block is ignored

        await using var db = Db();
        Assert.False(await db.Meters.AnyAsync());
    }

    [DatabaseFact]
    public async Task Importing_adds_the_meters_and_readings_and_the_pages_show_the_sheets_figures()
    {
        var client = app.ClientFor(UserId);

        var imported = await client.PostFormAsync("/Import?handler=Import", await PreviewAsync(client, Workbook()));

        Assert.Equal((HttpStatusCode.Redirect, "/"), (imported.Status, imported.Location));
        Assert.Contains("Imported 9 readings and 5 meters.", (await client.GetAsync("/")).Text);
        await using var db = Db();
        var meters = await db.Meters.Include(m => m.Readings).OrderBy(m => m.Utility).ThenBy(m => m.InstalledOn).ToListAsync();
        Assert.Equal(
        [
            (UtilityType.Electricity, null, null, 2), (UtilityType.Electricity, new DateOnly(2021, 3, 21), 0m, 2),
            (UtilityType.Gas, null, null, 2), (UtilityType.Gas, new DateOnly(2021, 3, 21), 0m, 1),
            (UtilityType.Water, null, null, 2)
        ], meters.OrderBy(m => m.Utility.ToString()).ThenBy(m => m.InstalledOn).Select(m => (m.Utility, m.InstalledOn, m.StartReading, m.Readings.Count)));
        Assert.Equal(MeterUnit.KilowattHours, meters.First(m => m.Utility == UtilityType.Electricity).Unit);
        Assert.Equal(240.5m, meters.SelectMany(m => m.Readings).Max(r => r.Date == new DateOnly(2021, 3, 31) ? r.Value : 0));
        Assert.Contains("9.05", (await client.GetAsync("/Electricity")).Text);   // 90.5 over the last 10 days
    }

    [DatabaseFact]
    public async Task Importing_again_adds_only_what_is_new()
    {
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Import?handler=Import", await PreviewAsync(client, Workbook()));
        var updated = new TestWorkbook()
            .Block("Electricity", R(2021, 2, 1, 5000), R(2021, 3, 1, 5280), R(2021, 3, 21, 150), R(2021, 3, 31, 240.5), R(2021, 4, 30, 540.5))
            .Build();

        var preview = await PreviewAsync(client, updated);
        Assert.Contains("1 reading to import: 30 Apr 2021 to 30 Apr 2021.", Card(preview, UtilityType.Electricity));
        Assert.Contains("4 on dates that already have a reading", Card(preview, UtilityType.Electricity));
        await client.PostFormAsync("/Import?handler=Import", preview);

        Assert.Contains("Imported 1 reading; 4 were already there and were left as they are.", (await client.GetAsync("/")).Text);
        await using var db = Db();
        Assert.Equal(2, await db.Meters.CountAsync(m => m.Utility == UtilityType.Electricity));
        Assert.Equal(5, await db.Readings.CountAsync(r => r.Meter!.Utility == UtilityType.Electricity));
    }

    [DatabaseFact]
    public async Task With_nothing_new_there_is_nothing_to_import()
    {
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Import?handler=Import", await PreviewAsync(client, Workbook()));

        var preview = await PreviewAsync(client, Workbook());
        Assert.Matches("<button type=\"submit\"[^>]*disabled", preview.Html);
        var again = await client.PostFormAsync("/Import?handler=Import", preview);

        Assert.Contains("There's nothing new to import", again.Errors);
        await using var db = Db();
        Assert.Equal(9, await db.Readings.CountAsync());
    }

    [DatabaseFact]
    public async Task One_users_import_isnt_anothers()
    {
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Import?handler=Import", await PreviewAsync(client, Workbook()));

        var stranger = app.ClientFor(NewUser());
        Assert.Contains("4 readings to import", Card(await PreviewAsync(stranger, Workbook()), UtilityType.Electricity));
    }

    [DatabaseFact]
    public async Task A_file_that_isnt_a_workbook_or_has_no_readings_is_refused()
    {
        var client = app.ClientFor(UserId);

        Assert.Contains("That isn't a spreadsheet this can read", (await PreviewAsync(client, "not a workbook"u8.ToArray())).Errors);
        Assert.Contains("No readings found",
            (await PreviewAsync(client, new TestWorkbook { Headings = ("When", "Value") }.Block("Gas", R(2021, 1, 1, 1)).Build())).Errors);
        Assert.Contains("Choose a spreadsheet",
            (await client.PostFormAsync("/Import?handler=Preview", await client.GetAsync("/Import"))).Errors);
    }
}
