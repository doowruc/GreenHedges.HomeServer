using System.Net;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Utilities.Tests.Pages;

public class DashboardPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>A utility's card, as text.</summary>
    private static string Card(PageResponse page, UtilityType utility) =>
        WebUtility.HtmlDecode(Regex.Replace(
            Regex.Match(page.Html, $"data-utility=\"{utility}\".*?</div></div>", RegexOptions.Singleline).Value, "<[^>]+>", " "));

    [DatabaseFact]
    public async Task With_no_readings_every_utility_says_so()
    {
        var page = await app.ClientFor(UserId).GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        foreach (var utility in Enum.GetValues<UtilityType>())
            Assert.Contains("No readings yet", Card(page, utility));
    }

    [DatabaseFact]
    public async Task Each_utility_shows_its_latest_reading_over_all_its_meters()
    {
        await using (var db = Db())
        {
            await TestData.AddMeterAsync(db, UtilityType.Electricity, readings: [(new(2020, 1, 1), 5000m), (new(2020, 2, 1), 5400m)]);
            await TestData.AddMeterAsync(db, UtilityType.Electricity, new DateOnly(2020, 2, 10), 0m, (new(2020, 3, 1), 1234.5m));
            await TestData.AddMeterAsync(db, UtilityType.Gas, readings: (new(2020, 3, 2), 87.125m));
        }

        var page = await app.ClientFor(UserId).GetAsync("/");

        var electricity = Card(page, UtilityType.Electricity);
        Assert.Contains("1,234.5 kWh", electricity);
        Assert.Contains("1 Mar 2020", electricity);
        Assert.Contains("3 readings", electricity);
        Assert.Contains("42.57 kWh a day", electricity);   // 1,234.5 since the replacement started at 0, over the 29 days since 1 February
        Assert.Contains("href=\"/Electricity\"", page.Html);
        var gas = Card(page, UtilityType.Gas);
        Assert.Contains("87.125 m³", gas);
        Assert.Contains("1 reading", gas);
        Assert.DoesNotContain("1 readings", gas);
        Assert.DoesNotContain("a day", gas);   // nothing to count from yet
        Assert.Contains("No readings yet", Card(page, UtilityType.Water));
    }

    [DatabaseFact]
    public async Task Another_users_readings_arent_shown()
    {
        await using (var db = Db()) await TestData.AddMeterAsync(db, readings: (new(2020, 1, 1), 5000m));

        var page = await app.ClientFor(NewUser()).GetAsync("/");

        Assert.Contains("No readings yet", Card(page, UtilityType.Electricity));
    }
}
