using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>A pension's transactions: importing made-up exports (<see cref="TestCsv"/>), the list by tax year, edit and delete.</summary>
public class TransactionPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    // Two overlapping years' exports: the 09/04/2020 interest is in both.
    private static readonly byte[] Older = TestCsv.Export(
        TestCsv.Interest("09/04/2020", "1.50"),
        TestCsv.Purchase("25/03/2020", "B1", "100", "250.000000", "-250.00"),
        TestCsv.Contribution("21/03/2020", "250.00"));

    private static readonly byte[] Newer = TestCsv.Export(
        TestCsv.Fee("06/05/2020", "-10.00"),
        TestCsv.Interest("09/04/2020", "1.50"));

    private async Task<(int PersonId, int PensionId)> PensionAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db), "Example SIPP", PensionType.Sipp);
        return (person.Id, pension.Id);
    }

    private static string Url((int PersonId, int PensionId) ids, string page = "") =>
        $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Transactions{page}";

    private static async Task<PageResponse> PreviewAsync(PageClient client, string url, params (string, byte[])[] files) =>
        await client.PostFilesAsync($"{url}?handler=Preview", await client.GetAsync(url), "uploads", files);

    [DatabaseFact]
    public async Task The_preview_counts_overlapping_files_once_and_saves_nothing()
    {
        var ids = await PensionAsync();

        var preview = await PreviewAsync(app.ClientFor(UserId), Url(ids, "/Import"), ("2019.csv", Older), ("2020.csv", Newer));

        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Contains("2019.csv: 3 transactions.", preview.Text);
        Assert.Contains("2020.csv: 2 transactions.", preview.Text);
        Assert.Contains("<strong>4</strong> new transactions to import.", preview.Html);
        Assert.Contains("Import 4 transactions", preview.Html);
        Assert.Contains(TestCsv.Fund, preview.Text);
        Assert.DoesNotContain("Test Person", preview.Text);   // the account's lines aren't shown
        await using var check = Db();
        Assert.False(await check.PensionTransactions.AnyAsync());
    }

    [DatabaseFact]
    public async Task Importing_adds_the_new_transactions_with_their_kinds_and_again_adds_nothing()
    {
        var ids = await PensionAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "/Import");

        var imported = await client.PostFormAsync($"{url}?handler=Import", await PreviewAsync(client, url, ("2019.csv", Older), ("2020.csv", Newer)));

        Assert.Equal(HttpStatusCode.Redirect, imported.Status);
        Assert.Equal($"{Url(ids)}?year=2020", imported.Location);   // the latest's tax year
        await using (var check = Db())
            Assert.Equal(
            [
                (new DateOnly(2020, 3, 21), TransactionKind.Contribution, (string?)null, 250m),
                (new DateOnly(2020, 3, 25), TransactionKind.Purchase, TestCsv.Fund, -250m),
                (new DateOnly(2020, 4, 9), TransactionKind.Interest, null, 1.5m),
                (new DateOnly(2020, 5, 6), TransactionKind.Fee, null, -10m),
            ], (await check.PensionTransactions.OrderBy(tx => tx.TradeDate).ToListAsync()).Select(tx => (tx.TradeDate, tx.Kind, tx.Fund, tx.Value)));

        var again = await PreviewAsync(client, url, ("2020.csv", Newer));
        Assert.Contains("<strong>0</strong> new transactions to import.", again.Html);
        Assert.Contains("2 the pension already has, which are left out.", again.Text);
        Assert.Contains("There's nothing new to import.", (await client.PostFormAsync($"{url}?handler=Import", again)).Errors);
    }

    [DatabaseFact]
    public async Task Unreadable_files_and_lines_are_reported()
    {
        var ids = await PensionAsync();
        var bad = TestCsv.Export(TestCsv.Line("31/02/2020", "X", "Something", "n/a", "n/a", "1.00"), TestCsv.Contribution("21/03/2020", "5.00"));

        var preview = await PreviewAsync(app.ClientFor(UserId), Url(ids, "/Import"), ("bad.csv", bad), ("notes.csv", "Hello,world\r\n"u8.ToArray()));

        Assert.Contains("bad.csv, line 7: the trade date \"31/02/2020\" isn't a date. It's skipped.", preview.Text);
        Assert.Contains("notes.csv can't be read: It has no heading row with Trade date and Value", preview.Text);
        Assert.Contains("<strong>1</strong> new transaction to import.", preview.Html);
    }

    [DatabaseFact]
    public async Task No_files_is_refused()
    {
        var ids = await PensionAsync();

        var preview = await PreviewAsync(app.ClientFor(UserId), Url(ids, "/Import"));

        Assert.Contains("Choose one or more transaction history files (.csv) to import.", preview.Errors);
    }

    [DatabaseFact]
    public async Task The_list_shows_a_tax_years_transactions_and_totals()
    {
        var ids = await PensionAsync();
        await using (var db = Db())
        {
            var pension = await db.Pensions.SingleAsync();
            await TestData.AddTransactionAsync(db, pension, new DateOnly(2020, 4, 5), 99m);   // the 2019-20 tax year
            await TestData.AddTransactionAsync(db, pension, new DateOnly(2020, 4, 6), 200m);
            await TestData.AddTransactionAsync(db, pension, new DateOnly(2020, 5, 6), 300m);
            await TestData.AddTransactionAsync(db, pension, new DateOnly(2020, 6, 1), 5000m, TransactionKind.TransferIn, "BACS", "Sipp Transfer");
            await TestData.AddTransactionAsync(db, pension, new DateOnly(2020, 6, 2), -5400m, TransactionKind.Purchase, "B2", "Example Fund 10 @ 100");
        }
        var client = app.ClientFor(UserId);

        var latest = await client.GetAsync(Url(ids));
        Assert.Equal(HttpStatusCode.OK, latest.Status);
        Assert.Contains("<option value=\"2020\" selected=\"selected\">2020-21</option>", latest.Html);
        Assert.Contains("2020-21 totals", latest.Text);
        Assert.Contains("Contribution <span class=\"text-body-secondary small\">(2)</span></th><td class=\"text-end\">£500.00</td>", latest.Html);
        Assert.Contains("Paid in</th><td class=\"text-end\">£5,500.00</td>", latest.Html);
        Assert.Contains("Change in cash</th><td class=\"text-end\">£100.00</td>", latest.Html);
        Assert.DoesNotContain("05/04/20", latest.Text);

        var before = await client.GetAsync($"{Url(ids)}?year=2019");
        Assert.Contains("05/04/20", before.Text);
        Assert.Contains("Paid in</th><td class=\"text-end\">£99.00</td>", before.Html);
    }

    [DatabaseFact]
    public async Task A_transactions_kind_and_fund_can_be_changed()
    {
        var ids = await PensionAsync();
        int id;
        await using (var db = Db())
            id = (await TestData.AddTransactionAsync(db, await db.Pensions.SingleAsync(), new DateOnly(2020, 6, 1), 7m, TransactionKind.Other, "X", "Something")).Id;
        var client = app.ClientFor(UserId);
        var url = Url(ids, $"/Edit/{id}");
        var form = await client.GetAsync(url);

        var saved = await client.PostFormAsync(url, form, new()
        {
            ["Transaction.Kind"] = form.OptionValue("Rebate"), ["Transaction.Fund"] = "  Example Fund  "
        });

        Assert.Equal($"{Url(ids)}?year=2020", saved.Location);
        await using var check = Db();
        var transaction = await check.PensionTransactions.SingleAsync();
        Assert.Equal((TransactionKind.Rebate, "Example Fund"), (transaction.Kind, transaction.Fund));
    }

    [DatabaseFact]
    public async Task A_transaction_can_be_deleted()
    {
        var ids = await PensionAsync();
        int id;
        await using (var db = Db())
            id = (await TestData.AddTransactionAsync(db, await db.Pensions.SingleAsync(), new DateOnly(2020, 6, 1), 7m)).Id;
        var client = app.ClientFor(UserId);
        var url = Url(ids, $"/Delete/{id}");

        var deleted = await client.PostFormAsync(url, await client.GetAsync(url));

        Assert.Equal($"{Url(ids)}?year=2020", deleted.Location);
        await using var check = Db();
        Assert.False(await check.PensionTransactions.AnyAsync());
    }

    [DatabaseFact]
    public async Task A_pension_or_person_with_transactions_cannot_be_deleted()
    {
        var ids = await PensionAsync();
        await using (var db = Db())
            await TestData.AddTransactionAsync(db, await db.Pensions.SingleAsync(), new DateOnly(2020, 6, 1), 7m);
        var client = app.ClientFor(UserId);

        var pension = await client.GetAsync($"/People/{ids.PersonId}/Pensions/Delete/{ids.PensionId}");
        Assert.Contains("can't be deleted while it has transactions (1)", pension.Text);
        await client.PostFormAsync($"/People/{ids.PersonId}/Pensions/Delete/{ids.PensionId}", pension);
        Assert.Contains("can't be deleted while their pensions have transactions (1)", (await client.GetAsync($"/People/Delete/{ids.PersonId}")).Text);

        await using var check = Db();
        Assert.True(await check.Pensions.AnyAsync());
        Assert.Matches($"<a [^>]*href=\"{Url(ids)}\"[^>]*>1</a>", (await client.GetAsync($"/People/{ids.PersonId}/Pensions")).Html);
    }

    [DatabaseFact]
    public async Task Another_persons_pension_and_transactions_are_not_found()
    {
        var ids = await PensionAsync();
        int samsPension, samsTransaction;
        await using (var db = Db())
        {
            var sam = await TestData.AddPersonAsync(db, "Sam");
            var pension = await TestData.AddPensionAsync(db, sam, await db.PensionProviders.SingleAsync());
            samsPension = pension.Id;
            samsTransaction = (await TestData.AddTransactionAsync(db, pension, new DateOnly(2020, 6, 1), 7m)).Id;
        }
        var client = app.ClientFor(UserId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(ids with { PensionId = samsPension }))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(ids with { PensionId = samsPension }, "/Import"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(ids, $"/Edit/{samsTransaction}"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(ids, $"/Delete/{samsTransaction}"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync(Url(ids))).Status);
    }
}
