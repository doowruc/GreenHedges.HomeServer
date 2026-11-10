using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Holdings;

/// <summary>
/// Sets the ISIN a fund's daily prices are fetched by (blank to stop), and fetches them at once. ?handler=Search looks
/// the fund up by name in the feed, to pick its ISIN from the matches.
/// </summary>
public partial class IsinModel(AppDbContext db, FundPrices prices) : PensionPageModel(db)
{
    [BindProperty(SupportsGet = true)]
    public string Fund { get; set; } = string.Empty;

    [BindProperty]
    public FundIsinInput Input { get; set; } = new();

    public bool FeedConfigured => prices.IsConfigured;

    /// <summary>What to search the feed for: the fund's name to start with.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    /// <summary>The search's matches; null before a search.</summary>
    public IReadOnlyList<FundMatch>? Matches { get; private set; }

    public string? SearchError { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPensionAsync() || !await IsPensionsFundAsync()) return NotFound();
        var existing = await Find().Select(f => new { f.Isin, f.RowVersion }).FirstOrDefaultAsync();
        if (existing is not null) Input = new FundIsinInput { Isin = existing.Isin, RowVersion = existing.RowVersion };
        Query ??= Fund;
        return Page();
    }

    public async Task<IActionResult> OnGetSearchAsync(CancellationToken cancellationToken)
    {
        var page = await OnGetAsync();
        if (page is not PageResult) return page;

        var query = Query?.Trim() ?? string.Empty;
        if (query.Length is < 3 or > 100) SearchError = "Search for between 3 and 100 characters of the fund's name.";
        else if (!prices.IsConfigured) SearchError = "The price feed has no key yet, so it can't be searched.";
        else
        {
            try
            {
                Matches = await prices.SearchAsync(query, cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                SearchError = "The search couldn't be run just now. Try again, or type the ISIN in.";
            }
        }
        return page;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadPensionAsync() || !await IsPensionsFundAsync()) return NotFound();

        var isin = Input.Isin?.Trim().ToUpperInvariant() ?? string.Empty;
        if (isin.Length > 0 && !IsinFormat().IsMatch(isin))
            ModelState.AddModelError("Input.Isin", "An ISIN is 12 characters: two letters, nine letters or digits, then a digit.");
        if (!ModelState.IsValid)
        {
            Query ??= Fund;
            return Page();
        }

        var existing = await Find().FirstOrDefaultAsync(cancellationToken);
        if (isin.Length == 0)
        {
            await Find().ExecuteDeleteAsync(cancellationToken);
            TempData["Message"] = $"Removed the ISIN for {Fund}.";
            return ToHoldings();
        }

        if (existing is null)
        {
            Db.PensionFunds.Add(new PensionFund { PensionId = PensionId, Fund = Fund, Isin = isin });
            await Db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            existing.Isin = isin;
            if (!await Db.TrySaveEditAsync(existing, Input.RowVersion))
            {
                ModelState.AddModelError(string.Empty, "Someone changed this ISIN after you opened it, so yours hasn't been saved. Reload the page to see the latest.");
                return Page();
            }
        }

        TempData["Message"] = await FetchAsync(isin, cancellationToken);
        return ToHoldings();
    }

    private async Task<string> FetchAsync(string isin, CancellationToken cancellationToken)
    {
        if (!prices.IsConfigured) return "Saved the ISIN. Its prices will start once the price feed has its key.";
        try
        {
            var added = await prices.UpdateAsync(isin, cancellationToken);
            return added == 0 ? "Saved the ISIN. There were no new prices." : $"Saved the ISIN and fetched {added} {(added == 1 ? "price" : "prices")}.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or DbUpdateException)
        {
            return "Saved the ISIN, but its prices couldn't be fetched. Check the ISIN: the feed tries again each morning.";
        }
    }

    private IQueryable<PensionFund> Find() => Db.PensionFunds.Where(f => f.PensionId == PensionId && f.Fund == Fund);

    private Task<bool> IsPensionsFundAsync() =>
        Db.PensionTransactions.Where(tx => tx.PensionId == PensionId).Deals().AnyAsync(tx => tx.Fund == Fund);

    private RedirectToPageResult ToHoldings() => RedirectToPage("Index", new { personId = PersonId, pensionId = PensionId });

    [GeneratedRegex("^[A-Z]{2}[A-Z0-9]{9}[0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex IsinFormat();
}
