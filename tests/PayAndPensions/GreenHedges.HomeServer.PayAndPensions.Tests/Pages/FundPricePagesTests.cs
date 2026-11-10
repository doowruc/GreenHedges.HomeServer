using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>A fund's ISIN and daily prices: made-up prices from <see cref="FakeFundPriceSource"/>.</summary>
public class FundPricePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private const string Fund = "Example Fund Class I";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // Prices are shared by ISIN, so each test has its own.
    private static string NewIsin() => "GB" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant() + "7";

    private async Task<(int PersonId, int PensionId)> PensionWithFundAsync()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var pension = await TestData.AddPensionAsync(db, person, await TestData.AddProviderAsync(db), "Example SIPP", PensionType.Sipp);
        var deal = await TestData.AddTransactionAsync(db, pension, Today.AddDays(-30), -200m, TransactionKind.Purchase, "B1", $"{Fund} 100 @ 200");
        (deal.Fund, deal.Quantity, deal.UnitPrice) = (Fund, 100m, 200m);
        await db.SaveChangesAsync();
        return (person.Id, pension.Id);
    }

    private static string Holdings((int PersonId, int PensionId) ids) => $"/People/{ids.PersonId}/Pensions/{ids.PensionId}/Holdings";

    private static string IsinUrl((int, int) ids, string fund = Fund) => $"{Holdings(ids)}/Isin?fund={Uri.EscapeDataString(fund)}";

    private static async Task<PageResponse> SaveAsync(PageClient client, string url, string isin) =>
        await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Input.Isin"] = isin });

    [DatabaseFact]
    public async Task Setting_an_isin_fetches_its_prices_and_values_the_fund_at_the_latest()
    {
        var ids = await PensionWithFundAsync();
        var isin = NewIsin();
        app.FundPriceSource.Set(isin, new SourcePrice(Today.AddDays(-20), 300m), new SourcePrice(Today.AddDays(-2), 389.333m));
        var client = app.ClientFor(UserId);

        var saved = await SaveAsync(client, IsinUrl(ids), $"  {isin.ToLowerInvariant()} ");

        Assert.Equal(Holdings(ids), saved.Location);
        var page = await client.GetAsync(Holdings(ids));
        Assert.Contains($"Saved the ISIN and fetched 2 prices.", page.Text);
        Assert.Contains(isin, page.Html);
        Assert.Contains("389.333", page.Html);
        Assert.Contains("<td class=\"text-end\">£389.33</td>", page.Html);     // 100 units at 389.33p

        var earlier = await client.GetAsync($"{Holdings(ids)}?asAt={Today.AddDays(-10):yyyy-MM-dd}");
        Assert.Contains("<td class=\"text-end\">£300.00</td>", earlier.Html);
    }

    [DatabaseFact]
    public async Task Saving_again_adds_only_the_new_prices()
    {
        var ids = await PensionWithFundAsync();
        var isin = NewIsin();
        var first = new SourcePrice(Today.AddDays(-3), 300m);
        app.FundPriceSource.Set(isin, first);
        var client = app.ClientFor(UserId);
        await SaveAsync(client, IsinUrl(ids), isin);

        app.FundPriceSource.Set(isin, first, new SourcePrice(Today.AddDays(-1), 310m));
        await SaveAsync(client, IsinUrl(ids), isin);

        await using var check = Db();
        Assert.Equal([300m, 310m], await check.FundPrices.Where(p => p.Isin == isin).OrderBy(p => p.Date).Select(p => p.Price).ToListAsync());
        Assert.Equal(isin, (await check.PensionFunds.SingleAsync()).Isin);
    }

    [DatabaseFact]
    public async Task A_bad_isin_is_refused_and_a_failed_fetch_still_saves_and_says_so()
    {
        var ids = await PensionWithFundAsync();
        var client = app.ClientFor(UserId);

        Assert.Contains("An ISIN is 12 characters", (await SaveAsync(client, IsinUrl(ids), "NOT AN ISIN")).Errors);

        var isin = NewIsin();   // the feed doesn't have it
        await SaveAsync(client, IsinUrl(ids), isin);

        Assert.Contains("its prices couldn't be fetched", (await client.GetAsync(Holdings(ids))).Text);
        await using var check = Db();
        Assert.Equal(isin, (await check.PensionFunds.SingleAsync()).Isin);
    }

    [DatabaseFact]
    public async Task Blank_removes_the_isin_and_the_last_deal_prices_it_again()
    {
        var ids = await PensionWithFundAsync();
        var isin = NewIsin();
        app.FundPriceSource.Set(isin, new SourcePrice(Today.AddDays(-2), 300m));
        var client = app.ClientFor(UserId);
        await SaveAsync(client, IsinUrl(ids), isin);

        await SaveAsync(client, IsinUrl(ids), "");

        Assert.Contains("<td class=\"text-end\">£200.00</td>", (await client.GetAsync(Holdings(ids))).Html);
        await using var check = Db();
        Assert.False(await check.PensionFunds.AnyAsync());
    }

    [DatabaseFact]
    public async Task Searching_by_name_lists_the_matches_and_using_one_saves_its_isin()
    {
        var ids = await PensionWithFundAsync();
        var isin = NewIsin();
        var name = $"Findable {isin} Fund";
        app.FundPriceSource.AddFund(new FundMatch(name, isin, "EUFUND", "GBX", 389.33m, "2026-01-15"));
        app.FundPriceSource.Set(isin, new SourcePrice(Today.AddDays(-2), 389.333m));
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync(IsinUrl(ids));
        Assert.Contains($"name=\"query\" value=\"{Fund}\"", form.Html);       // starts as the fund's name

        var found = await client.GetAsync($"{IsinUrl(ids)}&handler=Search&query={Uri.EscapeDataString($"findable {isin}")}");
        Assert.Contains(name, found.Text);
        Assert.Contains("389.33", found.Text);
        var use = System.Text.RegularExpressions.Regex.Match(found.Html, "<form method=\"post\"[^>]*>(?:(?!</form>).)*type=\"hidden\" name=\"Input.Isin\" value=\"" + isin + "\".*?</form>",
            System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.NotEmpty(use);

        var saved = await client.PostFormAsync(IsinUrl(ids), found with { Html = use });

        Assert.Equal(Holdings(ids), saved.Location);
        await using var check = Db();
        Assert.Equal(isin, (await check.PensionFunds.SingleAsync()).Isin);
        Assert.Equal(389.333m, (await check.FundPrices.SingleAsync(p => p.Isin == isin)).Price);
    }

    [DatabaseFact]
    public async Task A_fund_the_feed_prices_in_pounds_is_valued_in_pence_on_the_holdings_and_the_dashboard()
    {
        var ids = await PensionWithFundAsync();                    // 100 units bought at 200p
        var isin = NewIsin();
        app.FundPriceSource.AddFund(new FundMatch($"Pounds {isin} Fund", isin, "EUFUND", "GBP", 3.8933m, "2026-01-15"));
        app.FundPriceSource.Set(isin, new SourcePrice(Today.AddDays(-2), 3.8933m));
        var client = app.ClientFor(UserId);

        // By hand, unticked: the pounds are read as pence.
        await SaveAsync(client, IsinUrl(ids), isin);
        Assert.Contains("<td class=\"text-end\">£3.89</td>", (await client.GetAsync(Holdings(ids))).Html);

        // Using the search's match sets it from the currency.
        var found = await client.GetAsync($"{IsinUrl(ids)}&handler=Search&query={Uri.EscapeDataString($"pounds {isin}")}");
        Assert.Contains("GBP (pounds)", found.Text);
        var use = System.Text.RegularExpressions.Regex.Match(found.Html, "<form method=\"post\"[^>]*>(?:(?!</form>).)*type=\"hidden\" name=\"Input.Isin\" value=\"" + isin + "\".*?</form>",
            System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        await client.PostFormAsync(IsinUrl(ids), found with { Html = use });

        var holdings = await client.GetAsync(Holdings(ids));
        Assert.Contains("<td class=\"text-end\">£389.33</td>", holdings.Html);     // 100 units at 389.33p
        Assert.Contains("389.33", holdings.Text);
        Assert.Contains("£389.33", (await client.GetAsync($"/?person={ids.PersonId}")).Text);
        Assert.Contains("checked", System.Text.RegularExpressions.Regex.Match((await client.GetAsync(IsinUrl(ids))).Html, "<input[^>]*type=\"checkbox\"[^>]*>").Value);
        await using var check = Db();
        Assert.Equal(3.8933m, (await check.FundPrices.SingleAsync(p => p.Isin == isin)).Price);   // stored as fetched

        // Unticking it by hand goes back.
        await client.PostFormAsync(IsinUrl(ids), await client.GetAsync(IsinUrl(ids)), new() { ["Input.PricesInPounds"] = "false" });
        Assert.Contains("<td class=\"text-end\">£3.89</td>", (await client.GetAsync(Holdings(ids))).Html);
    }

    [DatabaseFact]
    public async Task A_search_with_no_matches_or_that_fails_or_is_too_short_says_so()
    {
        var ids = await PensionWithFundAsync();
        var client = app.ClientFor(UserId);
        string Search(string query) => $"{IsinUrl(ids)}&handler=Search&query={Uri.EscapeDataString(query)}";

        Assert.Contains("No funds with an ISIN match that.", (await client.GetAsync(Search("Nothing Like This " + Guid.NewGuid()))).Text);
        Assert.Contains("The search couldn't be run just now.", (await client.GetAsync(Search("this fails"))).Text);
        Assert.Contains("between 3 and 100 characters", (await client.GetAsync(Search("ab"))).Text);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync(Search("Example"))).Status);
    }

    [DatabaseFact]
    public async Task Only_the_pensions_own_funds_can_have_one()
    {
        var ids = await PensionWithFundAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync(IsinUrl(ids, "Some Other Fund"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(NewUser()).GetAsync(IsinUrl(ids))).Status);
    }
}
