using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Utilities.Tests.Pages;

/// <summary>A utility's meters: adding a replacement, editing and deleting.</summary>
public class MeterPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>The meters table, as rows of cell text (without the actions).</summary>
    private static List<List<string>> Meters(PageResponse page) =>
        Regex.Matches(Regex.Match(page.Html, "data-meters.*?</table>", RegexOptions.Singleline).Value, "<tr>.*?</tr>", RegexOptions.Singleline)
            .Select(row => Regex.Matches(row.Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Select(c => WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", " ")).Trim()).Take(4).ToList())
            .Where(cells => cells.Count > 0)
            .ToList();

    /// <summary>An electricity meter with readings on 1 February and 1 March 2021.</summary>
    private async Task<int> FirstMeterAsync()
    {
        await using var db = Db();
        return (await TestData.AddMeterAsync(db, readings: [(new(2021, 2, 1), 5000m), (new(2021, 3, 1), 5280m)])).Id;
    }

    private async Task<PageResponse> NewMeterAsync(PageClient client, string installedOn, string start, string serial = "") =>
        await client.PostFormAsync("/Electricity/Meters/New", await client.GetAsync("/Electricity/Meters/New"),
            new() { ["Meter.InstalledOn"] = installedOn, ["Meter.StartReading"] = start, ["Meter.SerialNumber"] = serial });

    [DatabaseFact]
    public async Task A_replacement_meter_is_added_and_the_next_reading_counts_from_its_start()
    {
        await FirstMeterAsync();
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync("/Electricity/Meters/New");
        Assert.Contains($"value=\"{DateTime.Today:yyyy-MM-dd}\"", form.Html);
        Assert.Matches("name=\"Meter.StartReading\"[^>]*value=\"0\"|value=\"0\"[^>]*name=\"Meter.StartReading\"", form.Html);
        var added = await NewMeterAsync(client, "2021-03-10", "0", "  TEST-0001 ");
        Assert.Equal((HttpStatusCode.Redirect, "/Electricity"), (added.Status, added.Location));

        var page = await client.GetAsync("/Electricity");
        Assert.Equal([["10 Mar 2021", "0", "TEST-0001", "0"], ["Not known", "", "", "2"]], Meters(page));
        await client.PostFormAsync("/Electricity", page, new() { ["Reading.Date"] = "2021-03-21", ["Reading.Value"] = "150" });
        Assert.Contains("7.50", (await client.GetAsync("/Electricity")).Text);   // 150 units in the 20 days since the old meter's last reading
    }

    [DatabaseTheory]
    [InlineData("2021-03-01", "0", "Another meter has a reading on or after then (01/03/2021): the date must be after its last reading.")]
    [InlineData("2021-02-15", "0", "Another meter has a reading on or after then (01/03/2021)")]
    [InlineData("2999-01-01", "0", "The date can't be in the future.")]
    [InlineData("", "0", "Enter the date it was installed.")]
    [InlineData("2021-03-10", "", "Enter what it showed when installed (usually 0).")]
    [InlineData("2021-03-10", "0.0001", "A reading has at most three decimal places.")]
    [InlineData("2021-03-10", "-1", "The start reading must be between 0 and 999,999,999.")]
    public async Task A_replacement_that_cant_be_right_is_refused(string installedOn, string start, string error)
    {
        await FirstMeterAsync();

        var page = await NewMeterAsync(app.ClientFor(UserId), installedOn, start);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(error, page.Errors);
        await using var check = Db();
        Assert.Equal(1, await check.Meters.CountAsync());
    }

    [DatabaseFact]
    public async Task Two_meters_cant_be_installed_on_the_same_day()
    {
        await FirstMeterAsync();
        var client = app.ClientFor(UserId);
        await NewMeterAsync(client, "2021-03-10", "0");

        Assert.Contains("Another meter was installed on 10/03/2021.", (await NewMeterAsync(client, "2021-03-10", "5")).Errors);
    }

    [DatabaseFact]
    public async Task The_first_meter_only_has_a_serial_number_to_edit()
    {
        var id = await FirstMeterAsync();
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync($"/Electricity/Meters/Edit/{id}");
        Assert.DoesNotContain("Meter.InstalledOn", form.Html);
        Assert.DoesNotContain("Delete meter", form.Html);   // it has readings
        var saved = await client.PostFormAsync($"/Electricity/Meters/Edit/{id}", form,
            new() { ["Meter.SerialNumber"] = "TEST-0002", ["Meter.InstalledOn"] = "2021-01-01", ["Meter.StartReading"] = "1" });

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        await using var check = Db();
        var meter = await check.Meters.SingleAsync();
        Assert.Equal(("TEST-0002", null, null), (meter.SerialNumber, meter.InstalledOn, meter.StartReading));   // a posted date is ignored
    }

    [DatabaseFact]
    public async Task A_replacements_date_and_start_can_change_while_every_reading_stays_on_its_meter()
    {
        await FirstMeterAsync();
        int id;
        await using (var db = Db())
            id = (await TestData.AddMeterAsync(db, UtilityType.Electricity, new DateOnly(2021, 3, 10), 0m, (new(2021, 3, 21), 150m))).Id;
        var client = app.ClientFor(UserId);
        var url = $"/Electricity/Meters/Edit/{id}";
        var form = await client.GetAsync(url);

        Assert.Contains("Another meter has a reading on or after then (01/03/2021)",
            (await client.PostFormAsync(url, form, new() { ["Meter.InstalledOn"] = "2021-03-01" })).Errors);
        Assert.Contains("This meter has a reading before then (21/03/2021): the date can't be after its first reading.",
            (await client.PostFormAsync(url, form, new() { ["Meter.InstalledOn"] = "2021-03-22" })).Errors);
        Assert.Contains("That's higher than this meter's first reading (150 on 21/03/2021).",
            (await client.PostFormAsync(url, form, new() { ["Meter.StartReading"] = "151" })).Errors);
        Assert.Contains("Enter the date it was installed.",
            (await client.PostFormAsync(url, form, new() { ["Meter.InstalledOn"] = "" })).Errors);

        var saved = await client.PostFormAsync(url, form, new() { ["Meter.InstalledOn"] = "2021-03-05", ["Meter.StartReading"] = "20" });
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        Assert.Contains("6.50", (await client.GetAsync("/Electricity")).Text);   // 130 units in 20 days
    }

    [DatabaseFact]
    public async Task A_meter_without_readings_can_be_deleted_and_one_with_readings_cant()
    {
        var withReadings = await FirstMeterAsync();
        var client = app.ClientFor(UserId);
        await NewMeterAsync(client, "2021-03-10", "0");
        int empty;
        await using (var db = Db()) empty = (await db.Meters.SingleAsync(m => m.InstalledOn != null)).Id;

        var form = await client.GetAsync($"/Electricity/Meters/Edit/{empty}");
        Assert.Contains("Delete meter", form.Html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/Electricity/Meters/Edit/{empty}?handler=Delete", form)).Status);
        await client.PostFormAsync($"/Electricity/Meters/Edit/{withReadings}?handler=Delete", await client.GetAsync($"/Electricity/Meters/Edit/{withReadings}"));

        Assert.Contains("A meter with readings can't be deleted.", (await client.GetAsync("/Electricity")).Text);
        await using var check = Db();
        Assert.Equal(withReadings, (await check.Meters.SingleAsync()).Id);
    }

    [DatabaseFact]
    public async Task A_meter_is_only_found_under_its_own_utility_and_user()
    {
        var id = await FirstMeterAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync($"/Gas/Meters/Edit/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync($"/Electricity/Meters/Edit/{id}")).Status);
    }

    [DatabaseFact]
    public async Task Another_users_meters_dont_limit_mine()
    {
        await FirstMeterAsync();   // readings up to 1 March 2021

        var stranger = app.ClientFor(NewUser());
        var added = await stranger.PostFormAsync("/Electricity/Meters/New", await stranger.GetAsync("/Electricity/Meters/New"),
            new() { ["Meter.InstalledOn"] = "2021-02-15", ["Meter.StartReading"] = "0" });

        Assert.Equal(HttpStatusCode.Redirect, added.Status);
    }
}
