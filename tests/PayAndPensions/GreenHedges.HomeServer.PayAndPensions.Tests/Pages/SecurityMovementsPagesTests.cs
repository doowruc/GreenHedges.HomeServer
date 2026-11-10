using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>Importing a fund's security movements: its cost adjustments and conversions, and how they show.</summary>
public class SecurityMovementsPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private const string ClassA = "Example Fund Class A", ClassI = "Example Fund Class I";

    private static readonly byte[] History = TestCsv.Export(
        TestCsv.Line("01/06/2023", "B1", $"{ClassA} 100 @  200.000000", "200.000000", "100", "-200.00"),
        TestCsv.Line("01/09/2023", "B2", $"{ClassI} 10 @  300.000000", "300.000000", "10", "-30.00"));

    private static readonly byte[] OldFund = TestCsv.Movements("Example Fund", "Class A",
        TestCsv.Movement("21/08/2023", "Conversion out", "X 1", "150.00", "100.0000", "-190.00"),
        TestCsv.Movement("30/06/2023", "Corp.A", "E 1", "0.00", "0.0000", "-10.00"),
        TestCsv.Movement("01/06/2023", "Bought", "B1", "200.00", "100.0000", "200.00"));

    private static readonly byte[] NewFund = TestCsv.Movements("Example Fund", "Class I",
        TestCsv.Movement("30/10/2023", "Corp.A", "E 2", "0.00", "0.0000", "-5.00"),
        TestCsv.Movement("01/09/2023", "Bought", "B2", "300.00", "10.0000", "30.00"),
        TestCsv.Movement("21/08/2023", "Conversion in", "X 1", "200.00", "95.0000", "190.00"));

    private async Task<(int PersonId, int PensionId)> PensionAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db), "Example SIPP", PensionType.Sipp);
        return (person.Id, pension.Id);
    }

    private static string Url((int PersonId, int PensionId) ids, string page) => $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/{page}";

    private static async Task<PageResponse> PreviewAsync(PageClient client, string url, params (string, byte[])[] files) =>
        await client.PostFilesAsync($"{url}?handler=Preview", await client.GetAsync(url), "uploads", files);

    private async Task ImportAllAsync(PageClient client, (int, int) ids)
    {
        var url = Url(ids, "Transactions/Import");
        await client.PostFormAsync($"{url}?handler=Import", await PreviewAsync(client, url, ("history.csv", History), ("a.csv", OldFund), ("i.csv", NewFund)));
    }

    [DatabaseFact]
    public async Task Movements_add_the_conversion_and_cost_adjustments_to_the_holdings_without_touching_cash()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);

        var preview = await PreviewAsync(client, Url(ids, "Transactions/Import"), ("history.csv", History), ("a.csv", OldFund), ("i.csv", NewFund));
        Assert.Contains("<strong>6</strong> new transactions to import.", preview.Html);   // the Bought lines are the history's
        Assert.DoesNotContain("Test Person", preview.Text);

        await ImportAllAsync(client, ids);

        var holdings = await client.GetAsync(Url(ids, "Holdings"));
        Assert.Contains($"<td>{ClassA} (converted)</td>", holdings.Html);
        Assert.Contains("<td class=\"text-end\">105</td>", holdings.Html);     // 95 received + 10 bought
        Assert.Contains("<td class=\"text-end\">£215.00</td>", holdings.Html); // 190 moved + 30 - 5

        var transactions = await client.GetAsync($"{Url(ids, "Transactions")}?year=2023");
        Assert.Contains("Cost adjustment <span class=\"text-body-secondary small\">(2)</span>", transactions.Html);
        Assert.Contains("Change in cash</th><td class=\"text-end\">£-230.00</td>", transactions.Html);

        var again = await PreviewAsync(client, Url(ids, "Transactions/Import"), ("a.csv", OldFund), ("i.csv", NewFund));
        Assert.Contains("<strong>0</strong> new transactions to import.", again.Html);
    }

    [DatabaseFact]
    public async Task A_movements_file_alone_says_its_purchases_and_sales_need_the_transaction_history()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "Transactions/Import");
        await ImportAllAsync(client, ids);
        var withANewPurchase = TestCsv.Movements("Example Fund", "Class I",
            TestCsv.Movement("09/11/2023", "Bought", "B3", "310.00", "10.0000", "31.00"),
            TestCsv.Movement("30/10/2023", "Corp.A", "E 2", "0.00", "0.0000", "-5.00"));

        var alone = await PreviewAsync(client, url, ("i.csv", withANewPurchase));
        var withHistory = await PreviewAsync(client, url, ("i.csv", withANewPurchase), ("history.csv", History));

        Assert.Contains("<strong>0</strong> new transactions to import.", alone.Html);
        Assert.Contains("1 bought or sold line isn't imported from a security movements file.", alone.Text);
        Assert.Contains("transaction history export", alone.Text);
        Assert.DoesNotContain("imported from a security movements file", withHistory.Text);
    }

    [DatabaseFact]
    public async Task A_files_conversion_replaces_one_recorded_by_hand_on_its_date()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var import = Url(ids, "Transactions/Import");
        await client.PostFormAsync($"{import}?handler=Import", await PreviewAsync(client, import, ("history.csv", History)));
        var convert = Url(ids, "Holdings/Convert");
        await client.PostFormAsync(convert, await client.GetAsync(convert), new()
        {
            ["Conversion.Date"] = "2023-08-21", ["Conversion.FromFund"] = ClassA, ["Conversion.ToFund"] = ClassI, ["Conversion.Units"] = "95",
        });

        var preview = await PreviewAsync(client, import, ("a.csv", OldFund), ("i.csv", NewFund));
        Assert.Contains("Replaces the conversion recorded by hand on 21/08/2023 with the file's.", preview.Text);
        await client.PostFormAsync($"{import}?handler=Import", preview);

        await using var check = Db();
        Assert.Equal(["X 1", "X 1"], await check.PensionTransactions.Where(tx => tx.Kind == TransactionKind.Conversion).Select(tx => tx.Reference).ToListAsync());
    }

    [DatabaseFact]
    public async Task A_cost_adjustment_cannot_be_edited_and_nothing_can_become_one()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        await ImportAllAsync(client, ids);
        int adjustment, purchase;
        await using (var db = Db())
        {
            adjustment = await db.PensionTransactions.Where(tx => tx.Kind == TransactionKind.CostAdjustment).Select(tx => tx.Id).FirstAsync();
            purchase = await db.PensionTransactions.Where(tx => tx.Kind == TransactionKind.Purchase).Select(tx => tx.Id).FirstAsync();
        }

        Assert.Contains("This is a cost adjustment", (await client.GetAsync(Url(ids, $"Transactions/Edit/{adjustment}"))).Text);
        Assert.DoesNotContain(">Cost adjustment</option>", (await client.GetAsync(Url(ids, $"Transactions/Edit/{purchase}"))).Html);
    }
}
