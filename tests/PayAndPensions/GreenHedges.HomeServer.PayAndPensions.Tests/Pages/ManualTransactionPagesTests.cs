using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>Transactions entered by hand, for a pension with no export: adding, changing, and what they do to holdings and payslips.</summary>
public class ManualTransactionPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private const string Fund = "Example Growth Fund";

    private async Task<(int PersonId, int PensionId)> PensionAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db));
        return (person.Id, pension.Id);
    }

    private static string Url((int PersonId, int PensionId) ids, string page) => $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/{page}";

    private static async Task<PageResponse> SaveAsync(PageClient client, string url, Dictionary<string, string> fields) =>
        await client.PostFormAsync(url, await client.GetAsync(url), fields);

    private static Dictionary<string, string> Deal(string kind, string date, string units, string? price = null, string? value = null, string fund = Fund)
    {
        var fields = new Dictionary<string, string>
        {
            ["Transaction.Kind"] = kind, ["Transaction.Date"] = date, ["Transaction.Fund"] = fund, ["Transaction.Units"] = units,
        };
        if (price is not null) fields["Transaction.UnitPrice"] = price;
        if (value is not null) fields["Transaction.Amount"] = value;
        return fields;
    }

    [DatabaseFact]
    public async Task A_purchase_added_by_hand_works_its_value_out_from_its_price_and_shows_in_the_holdings()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);

        var saved = await SaveAsync(client, Url(ids, "Transactions/Manual?fromHoldings=true"), Deal("Purchase", "2026-01-15", "100.5", price: "200"));

        Assert.Equal(Url(ids, "Holdings"), saved.Location);
        await using (var db = Db())
        {
            var tx = await db.PensionTransactions.SingleAsync();
            Assert.Equal((TransactionKind.Purchase, Fund, 100.5m, 200m, -201m, "MANUAL"), (tx.Kind, tx.Fund, tx.Quantity, tx.UnitPrice, tx.Value, tx.Reference));
            Assert.Equal($"{Fund} 100.5 @ 200", tx.Description);
        }
        var holdings = await client.GetAsync(Url(ids, "Holdings"));
        Assert.Contains("Added the transaction on 15/01/2026.", holdings.Text);
        Assert.Contains($"<td>{Fund}</td>", holdings.Html);
        Assert.Contains("<td class=\"text-end\">£201.00</td>", holdings.Html);
    }

    [DatabaseFact]
    public async Task A_deal_with_its_value_works_out_its_price_and_a_sale_is_money_in_under_the_funds_existing_name()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "Transactions/Manual");

        await SaveAsync(client, url, Deal("Purchase", "2026-01-15", "300", value: "1000"));
        var sale = await SaveAsync(client, url, Deal("Sale", "2026-02-15", "50", value: "200", fund: Fund.ToUpperInvariant()));

        Assert.Equal(Url(ids, "Transactions?year=2025"), sale.Location);
        await using var db = Db();
        var lines = await db.PensionTransactions.OrderBy(tx => tx.TradeDate).ToListAsync();
        Assert.Equal((-1000m, 333.333333m), (lines[0].Value, lines[0].UnitPrice));
        Assert.Equal((TransactionKind.Sale, Fund, 200m, 400m), (lines[1].Kind, lines[1].Fund, lines[1].Value, lines[1].UnitPrice));
    }

    [DatabaseFact]
    public async Task One_added_by_hand_can_be_changed_in_full_but_an_imported_one_cannot()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        await SaveAsync(client, Url(ids, "Transactions/Manual"), Deal("Purchase", "2026-01-15", "100", price: "200"));
        int manual, imported;
        await using (var db = Db())
        {
            manual = (await db.PensionTransactions.SingleAsync()).Id;
            imported = (await TestData.AddTransactionAsync(db, new Pension { Id = ids.PensionId }, new DateOnly(2026, 1, 20), 50m)).Id;
        }

        var viaEdit = await client.GetAsync(Url(ids, $"Transactions/Edit/{manual}"));
        Assert.Equal(Url(ids, $"Transactions/Manual/{manual}"), viaEdit.Location);
        var form = await client.GetAsync(Url(ids, $"Transactions/Manual/{manual}"));
        Assert.Contains("value=\"200.00\"", form.Html);          // the amount, without its sign
        var changed = await client.PostFormAsync(Url(ids, $"Transactions/Manual/{manual}"), form, new()
        {
            ["Transaction.Kind"] = "Purchase", ["Transaction.Date"] = "2026-01-16", ["Transaction.Units"] = "110", ["Transaction.Amount"] = "220",
            ["Transaction.Description"] = "First payment",
        });

        Assert.Equal(HttpStatusCode.Redirect, changed.Status);
        await using var check = Db();
        var tx = await check.PensionTransactions.SingleAsync(t => t.Id == manual);
        Assert.Equal((new DateOnly(2026, 1, 16), 110m, -220m, "First payment"), (tx.TradeDate, tx.Quantity, tx.Value, tx.Description));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(ids, $"Transactions/Manual/{imported}"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url(ids, $"Transactions/Edit/{imported}"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync(Url(ids, $"Transactions/Manual/{manual}"))).Status);
    }

    [DatabaseFact]
    public async Task A_cash_line_keeps_no_fund_and_takes_its_sign_from_its_kind()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "Transactions/Manual");

        await SaveAsync(client, url, new() { ["Transaction.Kind"] = "Fee", ["Transaction.Date"] = "2026-01-15", ["Transaction.Amount"] = "12.50", ["Transaction.Fund"] = Fund, ["Transaction.Units"] = "3" });
        await SaveAsync(client, url, new() { ["Transaction.Kind"] = "Other", ["Transaction.Date"] = "2026-01-16", ["Transaction.Amount"] = "-5" });

        await using var db = Db();
        var lines = await db.PensionTransactions.OrderBy(tx => tx.TradeDate).ToListAsync();
        Assert.Equal((TransactionKind.Fee, null, null, -12.5m, "Fee"), (lines[0].Kind, lines[0].Fund, lines[0].Quantity, lines[0].Value, lines[0].Description));
        Assert.Equal((TransactionKind.Other, -5m), (lines[1].Kind, lines[1].Value));
    }

    [DatabaseFact]
    public async Task A_contribution_sets_the_received_date_on_the_payslip_it_adds_up_to()
    {
        (int PersonId, int PensionId) ids;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db));
            await TestData.AddPayslipAsync(db, person, 2020, 1, ("Salary", 2000m), ("Pension", 100m), ("Employer pension", 150m));   // paid 25/04/2020
            await db.Payslips.ExecuteUpdateAsync(s => s.SetProperty(p => p.PensionId, pension.Id));
            ids = (person.Id, pension.Id);
        }
        var client = app.ClientFor(UserId);

        var saved = await SaveAsync(client, Url(ids, "Transactions/Manual"),
            new() { ["Transaction.Kind"] = "Contribution", ["Transaction.Date"] = "2020-05-21", ["Transaction.Amount"] = "250" });

        Assert.Contains("set the pension received date on 1 payslip", (await client.GetAsync(saved.Location!)).Text);
        await using var check = Db();
        Assert.Equal(new DateOnly(2020, 5, 21), (await check.Payslips.SingleAsync()).PensionReceivedDate);
    }

    [DatabaseFact]
    public async Task What_is_missing_or_wrong_is_said_and_nothing_is_saved()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "Transactions/Manual");
        var tomorrow = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");

        var empty = await SaveAsync(client, url, new() { ["Transaction.Kind"] = "Purchase", ["Transaction.Date"] = tomorrow });
        var negative = await SaveAsync(client, url, new() { ["Transaction.Kind"] = "Contribution", ["Transaction.Date"] = "2026-01-15", ["Transaction.Amount"] = "-10" });
        var conversion = await SaveAsync(client, url, new() { ["Transaction.Kind"] = "Conversion", ["Transaction.Date"] = "2026-01-15", ["Transaction.Amount"] = "10" });

        Assert.Contains("The date can't be in the future.", empty.Errors);
        Assert.Contains("Give the fund bought or sold.", empty.Errors);
        Assert.Contains("Give the units bought or sold.", empty.Errors);
        Assert.Contains("Give the value, or the price to work it out from.", empty.Errors);
        Assert.Contains("without a minus sign", negative.Errors);
        Assert.Contains("Choose a kind.", conversion.Errors);
        await using var db = Db();
        Assert.False(await db.PensionTransactions.AnyAsync());
    }

    [DatabaseFact]
    public async Task The_holdings_and_transactions_pages_link_to_adding_one()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);

        Assert.Contains($"href=\"{Url(ids, "Transactions/Manual")}?fromHoldings=true\"", (await client.GetAsync(Url(ids, "Holdings"))).Html);
        Assert.Contains($"href=\"{Url(ids, "Transactions/Manual")}\"", (await client.GetAsync(Url(ids, "Transactions"))).Html);
        Assert.Contains("/js/transaction-form", (await client.GetAsync(Url(ids, "Transactions/Manual"))).Html);
    }
}
