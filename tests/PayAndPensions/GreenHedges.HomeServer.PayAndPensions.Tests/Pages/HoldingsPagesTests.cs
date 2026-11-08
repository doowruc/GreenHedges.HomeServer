using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>A pension's units held per fund, from its purchases and sales.</summary>
public class HoldingsPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private async Task<(int PersonId, int PensionId)> PensionAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db), "Example SIPP", PensionType.Sipp);
        return (person.Id, pension.Id);
    }

    private static string Url((int PersonId, int PensionId) ids) => $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Holdings";

    [DatabaseFact]
    public async Task Imported_purchases_and_sales_give_each_funds_units_and_value()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var import = $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Transactions/Import";
        var export = TestCsv.Export(
            TestCsv.Contribution("01/06/2020", "1000.00"),
            TestCsv.Purchase("02/06/2020", "B1", "400", "200.000000", "-800.00"),
            TestCsv.Purchase("02/07/2020", "B2", "100", "150.000000", "-150.00"),
            TestCsv.Line("03/08/2020", "S1", $"{TestCsv.Fund} 50 @  180.000000", "180.000000", "50", "90.00"));
        var preview = await client.PostFilesAsync($"{import}?handler=Preview", await client.GetAsync(import), "uploads", [("2020.csv", export)]);
        await client.PostFormAsync($"{import}?handler=Import", preview);

        var page = await client.GetAsync(Url(ids));

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains($"<td>{TestCsv.Fund}</td>", page.Html);
        Assert.Contains("<td class=\"text-end\">450</td>", page.Html);         // units: 400 + 100 - 50
        Assert.Contains("<td class=\"text-end\">£810.00</td>", page.Html);     // 450 at 180p
        Assert.Contains("<td class=\"text-end\">£855.00</td>", page.Html);     // cost: 950 for 500 units, less the 50 sold's share
        Assert.Contains("£-45.00", page.Html);                                 // gain

        var earlier = await client.GetAsync($"{Url(ids)}?asAt=2020-06-30");
        Assert.Contains("<td class=\"text-end\">400</td>", earlier.Html);
        Assert.Contains("<td class=\"text-end\">£800.00</td>", earlier.Html);
    }

    [DatabaseFact]
    public async Task Without_deals_it_says_so_and_deals_without_a_fund_are_flagged()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        Assert.Contains("No purchases or sales of funds.", (await client.GetAsync(Url(ids))).Text);

        await using (var db = Db())
        {
            var tx = await TestData.AddTransactionAsync(db, await db.Pensions.SingleAsync(), new DateOnly(2020, 6, 1), -10m, TransactionKind.Purchase, "B1", "Something");
            tx.Quantity = 5;
            await db.SaveChangesAsync();
        }

        Assert.Contains("1 purchase or sale has no fund, so isn't counted", (await client.GetAsync(Url(ids))).Text);
    }

    [DatabaseFact]
    public async Task Another_persons_holdings_are_not_found()
    {
        var ids = await PensionAsync();
        int samsPension;
        await using (var db = Db())
            samsPension = (await TestData.AddPensionAsync(db, await TestData.AddPersonAsync(db, "Sam"), await db.PensionProviders.SingleAsync())).Id;
        var client = app.ClientFor(UserId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(ids with { PensionId = samsPension }))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync(Url(ids))).Status);
    }
}
