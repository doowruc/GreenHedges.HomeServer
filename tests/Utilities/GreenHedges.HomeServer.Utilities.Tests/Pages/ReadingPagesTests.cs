using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Utilities.Tests.Pages;

/// <summary>A utility's page: its readings table and the forms to add, edit and delete a reading.</summary>
public class ReadingPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>The readings table, as rows of cell text (without the actions).</summary>
    private static List<List<string>> Table(PageResponse page) =>
        Regex.Matches(Regex.Match(page.Html, "data-readings.*?</table>", RegexOptions.Singleline).Value, "<tr>.*?</tr>", RegexOptions.Singleline)
            .Select(row => Regex.Matches(row.Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Select(c => WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", " ")).Trim()).Take(5).ToList())
            .Where(cells => cells.Count > 0)
            .ToList();

    private async Task<PageResponse> AddAsync(PageClient client, string utility, string date, string value) =>
        await client.PostFormAsync($"/{utility}", await client.GetAsync($"/{utility}"), new() { ["Reading.Date"] = date, ["Reading.Value"] = value });

    [DatabaseFact]
    public async Task Readings_are_listed_latest_first_with_units_days_and_units_a_day()
    {
        await using (var db = Db())
            await TestData.AddMeterAsync(db, UtilityType.Electricity,
                readings: [(new(2021, 1, 1), 1000m), (new(2021, 1, 31), 1300m), (new(2021, 2, 10), 1350.5m)]);

        var page = await app.ClientFor(UserId).GetAsync("/Electricity");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(
        [
            ["10 Feb 2021", "1,350.5", "50.5", "10", "5.05"],
            ["31 Jan 2021", "1,300", "300", "30", "10.00"],
            ["1 Jan 2021", "1,000", "", "", ""],
        ], Table(page));
        Assert.Contains("kWh a day", page.Text);
        Assert.Contains("1,350.5 kWh", page.Text);
    }

    [DatabaseFact]
    public async Task Water_is_shown_in_litres_a_day()
    {
        await using (var db = Db())
            await TestData.AddMeterAsync(db, UtilityType.Water, readings: [(new(2021, 1, 1), 100m), (new(2021, 1, 11), 105m)]);

        var page = await app.ClientFor(UserId).GetAsync("/Water");

        Assert.Equal(["11 Jan 2021", "105", "5", "10", "500"], Table(page)[0]);
        Assert.Contains("Litres a day", page.Text);
        Assert.Contains("500 litres", page.Text);
    }

    [DatabaseFact]
    public async Task A_replacement_meters_first_reading_is_marked_and_counts_from_its_start()
    {
        await using (var db = Db())
        {
            await TestData.AddMeterAsync(db, UtilityType.Gas, readings: [(new(2021, 2, 1), 5000m), (new(2021, 3, 1), 5280m)]);
            await TestData.AddMeterAsync(db, UtilityType.Gas, new DateOnly(2021, 3, 10), 0m, (new(2021, 3, 21), 150m));
        }

        var page = await app.ClientFor(UserId).GetAsync("/Gas");

        Assert.Equal(["21 Mar 2021 New meter", "150", "150", "20", "7.50"], Table(page)[0]);
        Assert.Equal(["1 Mar 2021", "5,280", "280", "28", "10.00"], Table(page)[1]);
    }

    [DatabaseFact]
    public async Task The_first_reading_added_brings_the_utilitys_first_meter_with_it()
    {
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync("/Gas");
        Assert.Contains("No gas readings yet", form.Text);
        Assert.Contains($"value=\"{DateTime.Today:yyyy-MM-dd}\"", form.Html);   // today, to change
        var added = await client.PostFormAsync("/Gas", form, new() { ["Reading.Date"] = "2021-01-01", ["Reading.Value"] = "326.725" });

        Assert.Equal(HttpStatusCode.Redirect, added.Status);
        Assert.Equal("/Gas", added.Location);
        await using var db = Db();
        var meter = await db.Meters.Include(m => m.Readings).SingleAsync();
        Assert.Equal((UtilityType.Gas, MeterUnit.CubicMetres, null, null), (meter.Utility, meter.Unit, meter.InstalledOn, meter.StartReading));
        Assert.Equal((new DateOnly(2021, 1, 1), 326.725m), (meter.Readings[0].Date, meter.Readings[0].Value));
    }

    [DatabaseFact]
    public async Task Later_readings_go_on_the_same_meter_and_show_what_was_used()
    {
        var client = app.ClientFor(UserId);
        await AddAsync(client, "Electricity", "2021-01-01", "1000");
        await AddAsync(client, "Electricity", "2021-01-11", "1100");

        await using (var db = Db()) Assert.Equal(1, await db.Meters.CountAsync());
        var page = await client.GetAsync("/Electricity");
        Assert.Equal(["11 Jan 2021", "1,100", "100", "10", "10.00"], Table(page)[0]);
        Assert.Contains("Added the electricity reading for 11/01/2021.", page.Text);
    }

    [DatabaseFact]
    public async Task Each_utility_has_its_own_meter_and_readings()
    {
        var client = app.ClientFor(UserId);
        await AddAsync(client, "Electricity", "2021-01-01", "1000");
        await AddAsync(client, "Water", "2021-01-01", "50");

        Assert.Equal(["1 Jan 2021", "50", "", "", ""], Assert.Single(Table(await client.GetAsync("/Water"))));
        Assert.Empty(Table(await client.GetAsync("/Gas")));
    }

    [DatabaseTheory]
    [InlineData("2021-01-11", "1100", "There's already a reading for 11/01/2021.")]
    [InlineData("2021-01-21", "1099", "That's lower than the reading before it (1,100 on 11/01/2021). If the meter was replaced, add the new meter first.")]
    [InlineData("2021-01-05", "1101", "That's higher than the reading after it (1,100 on 11/01/2021).")]
    [InlineData("2021-01-05", "999", "That's lower than the reading before it (1,000 on 01/01/2021)")]
    [InlineData("2021-01-21", "1200.1234", "A reading has at most three decimal places.")]
    [InlineData("2021-01-21", "", "Enter the reading.")]
    [InlineData("2021-01-21", "-1", "The reading must be between 0 and 999,999,999.")]
    [InlineData("2999-01-01", "1200", "The date can't be in the future.")]
    public async Task A_reading_that_cant_be_right_is_refused(string date, string value, string error)
    {
        await using (var db = Db())
            await TestData.AddMeterAsync(db, readings: [(new(2021, 1, 1), 1000m), (new(2021, 1, 11), 1100m)]);

        var page = await AddAsync(app.ClientFor(UserId), "Electricity", date, value);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(error, page.Errors);
        Assert.Equal(2, Table(page).Count);   // the table is still there, unchanged
        await using var check = Db();
        Assert.Equal(2, await check.Readings.CountAsync());
    }

    [DatabaseFact]
    public async Task A_reading_between_two_others_is_fine_when_it_fits()
    {
        await using (var db = Db())
            await TestData.AddMeterAsync(db, readings: [(new(2021, 1, 1), 1000m), (new(2021, 1, 11), 1100m)]);
        var client = app.ClientFor(UserId);

        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(client, "Electricity", "2021-01-06", "1040")).Status);

        Assert.Equal(["11 Jan 2021", "1,100", "60", "5", "12.00"], Table(await client.GetAsync("/Electricity"))[0]);
    }

    [DatabaseFact]
    public async Task A_reading_from_a_replacements_installed_date_goes_on_it_and_cant_be_below_its_start()
    {
        int oldId, newId;
        await using (var db = Db())
        {
            oldId = (await TestData.AddMeterAsync(db, readings: (new(2021, 3, 1), 5280m))).Id;
            newId = (await TestData.AddMeterAsync(db, UtilityType.Electricity, new DateOnly(2021, 3, 10), 10m)).Id;
        }
        var client = app.ClientFor(UserId);

        Assert.Contains("That's lower than the meter's start reading (10).", (await AddAsync(client, "Electricity", "2021-03-21", "9")).Errors);
        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(client, "Electricity", "2021-03-21", "150")).Status);
        // Before the replacement was installed: still the old meter, whose readings carry on from its last.
        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(client, "Electricity", "2021-03-09", "5300")).Status);

        await using var check = Db();
        Assert.Equal(newId, (await check.Readings.SingleAsync(r => r.Value == 150m)).MeterId);
        Assert.Equal(oldId, (await check.Readings.SingleAsync(r => r.Value == 5300m)).MeterId);
    }

    [DatabaseFact]
    public async Task A_reading_is_edited_and_deleted()
    {
        int id;
        await using (var db = Db())
            id = (await TestData.AddMeterAsync(db, readings: [(new(2021, 1, 1), 1000m), (new(2021, 1, 11), 1100m)])).Readings[1].Id;
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync($"/Electricity/Edit/{id}");
        Assert.Contains("value=\"2021-01-11\"", form.Html);
        Assert.Contains("lower than the reading before it", (await client.PostFormAsync($"/Electricity/Edit/{id}", form, new() { ["Reading.Value"] = "900" })).Errors);
        var edited = await client.PostFormAsync($"/Electricity/Edit/{id}", form, new() { ["Reading.Date"] = "2021-01-21", ["Reading.Value"] = "1150" });
        Assert.Equal((HttpStatusCode.Redirect, "/Electricity"), (edited.Status, edited.Location));
        Assert.Equal(["21 Jan 2021", "1,150", "150", "20", "7.50"], Table(await client.GetAsync("/Electricity"))[0]);

        var confirm = await client.GetAsync($"/Electricity/Delete/{id}");
        Assert.Contains("the electricity reading for 21/01/2021, 1,150 kWh", confirm.Text);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/Electricity/Delete/{id}", confirm)).Status);
        Assert.Equal(["1 Jan 2021", "1,000", "", "", ""], Assert.Single(Table(await client.GetAsync("/Electricity"))));
    }

    [DatabaseFact]
    public async Task Saving_the_same_date_and_value_again_isnt_a_clash_with_itself()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddMeterAsync(db, readings: (new(2021, 1, 1), 1000m))).Readings[0].Id;
        var client = app.ClientFor(UserId);

        var saved = await client.PostFormAsync($"/Electricity/Edit/{id}", await client.GetAsync($"/Electricity/Edit/{id}"));

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
    }

    [DatabaseFact]
    public async Task An_edit_made_on_an_out_of_date_form_isnt_saved()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddMeterAsync(db, readings: (new(2021, 1, 1), 1000m))).Readings[0].Id;
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync($"/Electricity/Edit/{id}");
        await client.PostFormAsync($"/Electricity/Edit/{id}", form, new() { ["Reading.Value"] = "1001" });

        var stale = await client.PostFormAsync($"/Electricity/Edit/{id}", form, new() { ["Reading.Value"] = "1002" });

        Assert.Contains("Someone changed this reading", stale.Errors);
        await using var check = Db();
        Assert.Equal(1001m, (await check.Readings.SingleAsync()).Value);
    }

    [DatabaseFact]
    public async Task A_reading_is_only_found_under_its_own_utility_and_user()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddMeterAsync(db, readings: (new(2021, 1, 1), 1000m))).Readings[0].Id;

        var client = app.ClientFor(UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Gas/Edit/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Gas/Delete/{id}")).Status);
        var stranger = app.ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/Electricity/Edit/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/Electricity/Delete/{id}")).Status);
        Assert.Empty(Table(await stranger.GetAsync("/Electricity")));

        // Posting the delete as someone else, or under another utility, deletes nothing.
        await stranger.PostFormAsync($"/Electricity/Delete/{id}", await client.GetAsync($"/Electricity/Delete/{id}"));
        await client.PostFormAsync($"/Gas/Delete/{id}", await client.GetAsync($"/Electricity/Delete/{id}"));
        await using var check = Db();
        Assert.True(await check.Readings.AnyAsync(r => r.Id == id));
    }

    [DatabaseTheory]
    [InlineData("/Oil")]
    [InlineData("/0")]
    [InlineData("/Oil/Meters/New")]
    public async Task Only_the_three_utilities_have_pages(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync(url)).Status);

    [DatabaseFact]
    public async Task The_address_works_in_any_case() =>
        Assert.Equal(HttpStatusCode.OK, (await app.ClientFor(UserId).GetAsync("/electricity")).Status);
}
