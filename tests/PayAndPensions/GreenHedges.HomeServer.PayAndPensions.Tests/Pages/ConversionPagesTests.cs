using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>Recording a fund conversion, which the exports leave out, and how it shows, edits and deletes.</summary>
public class ConversionPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private const string ClassA = "Example Fund Class A", ClassI = "Example Fund Class I";

    private async Task<(int PersonId, int PensionId)> PensionWithDealsAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db), "Example SIPP", PensionType.Sipp);
        async Task Buy(DateOnly date, string fund, decimal units, decimal price)
        {
            var tx = await TestData.AddTransactionAsync(db, pension, date, -units * price / 100, TransactionKind.Purchase, "B1", $"{fund} {units} @ {price}");
            (tx.Fund, tx.Quantity, tx.UnitPrice) = (fund, units, price);
            await db.SaveChangesAsync();
        }
        await Buy(new DateOnly(2023, 6, 1), ClassA, 100, 200);
        await Buy(new DateOnly(2023, 7, 1), ClassA, 50, 220);
        await Buy(new DateOnly(2023, 9, 1), ClassI, 10, 250);
        return (person.Id, pension.Id);
    }

    private static string Url((int PersonId, int PensionId) ids, string page = "") => $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Holdings{page}";

    private static Dictionary<string, string> Form(string date, string from, string to, string units) => new()
    {
        ["Conversion.Date"] = date, ["Conversion.FromFund"] = from, ["Conversion.ToFund"] = to, ["Conversion.Units"] = units,
    };

    [DatabaseFact]
    public async Task A_conversion_moves_the_units_and_cost_to_the_new_fund()
    {
        var ids = await PensionWithDealsAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "/Convert");

        var saved = await client.PostFormAsync(url, await client.GetAsync(url), Form("2023-08-21", ClassA, "example fund class i", "140.5"));

        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        Assert.Equal(Url(ids), saved.Location);
        await using (var check = Db())
            Assert.Equal(
            [
                (ClassA, -150m, 310m, $"Converted to {ClassI}"),
                (ClassI, 140.5m, -310m, $"Converted from {ClassA}"),   // the existing fund's name
            ], (await check.PensionTransactions.Where(tx => tx.Kind == TransactionKind.Conversion).OrderBy(tx => tx.Value).ToListAsync())
                .OrderBy(tx => tx.Fund).Select(tx => (tx.Fund, tx.Quantity!.Value, tx.Value, tx.Description)));

        var holdings = await client.GetAsync(Url(ids));
        Assert.Contains($"<td>{ClassA} (converted)</td>", holdings.Html);
        Assert.Contains("<td class=\"text-end\">150.5</td>", holdings.Html);   // 140.5 received + 10 bought
        Assert.Contains("<td class=\"text-end\">£335.00</td>", holdings.Html); // cost: 310 moved + 25

        var transactions = await client.GetAsync($"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Transactions?year=2023");
        Assert.Contains("Conversion <span class=\"text-body-secondary small\">(2)</span></th><td class=\"text-end\">£0.00</td>", transactions.Html);
    }

    [DatabaseFact]
    public async Task A_conversion_needs_units_held_on_the_date_and_another_fund()
    {
        var ids = await PensionWithDealsAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "/Convert");
        var form = await client.GetAsync(url);

        Assert.Contains("The pension held none of Example Fund Class I on 21/08/2023.",
            (await client.PostFormAsync(url, form, Form("2023-08-21", ClassI, ClassA, "5"))).Errors);
        Assert.Contains("Choose a different fund from the one converted.",
            (await client.PostFormAsync(url, form, Form("2023-08-21", ClassA, ClassA.ToUpperInvariant(), "5"))).Errors);
        Assert.Contains("Give the units to at most 6 decimal places.",
            (await client.PostFormAsync(url, form, Form("2023-08-21", ClassA, ClassI, "1.0000001"))).Errors);
        await using var check = Db();
        Assert.False(await check.PensionTransactions.AnyAsync(tx => tx.Kind == TransactionKind.Conversion));
    }

    [DatabaseFact]
    public async Task A_conversion_cannot_be_edited_and_deletes_both_lines()
    {
        var ids = await PensionWithDealsAsync();
        var client = app.ClientFor(UserId);
        var url = Url(ids, "/Convert");
        await client.PostFormAsync(url, await client.GetAsync(url), Form("2023-08-21", ClassA, ClassI, "140"));
        int conversionId, purchaseId;
        await using (var db = Db())
        {
            conversionId = await db.PensionTransactions.Where(tx => tx.Kind == TransactionKind.Conversion).Select(tx => tx.Id).FirstAsync();
            purchaseId = await db.PensionTransactions.Where(tx => tx.Kind == TransactionKind.Purchase).Select(tx => tx.Id).FirstAsync();
        }
        var transactions = $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Transactions";

        var edit = await client.GetAsync($"{transactions}/Edit/{conversionId}");
        Assert.Contains("This is a fund conversion, so it can't be changed", edit.Text);
        Assert.DoesNotContain(">Conversion</option>", (await client.GetAsync($"{transactions}/Edit/{purchaseId}")).Html);

        var delete = await client.GetAsync($"{transactions}/Delete/{conversionId}");
        Assert.Contains("the fund conversion on 21/08/2023 (both its lines)", delete.Text);
        await client.PostFormAsync($"{transactions}/Delete/{conversionId}", delete);

        await using var check = Db();
        Assert.False(await check.PensionTransactions.AnyAsync(tx => tx.Kind == TransactionKind.Conversion));
        Assert.Equal(3, await check.PensionTransactions.CountAsync());
    }

    [DatabaseFact]
    public async Task Another_persons_pension_cannot_be_converted()
    {
        var ids = await PensionWithDealsAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync(Url(ids, "/Convert"))).Status);
    }
}
