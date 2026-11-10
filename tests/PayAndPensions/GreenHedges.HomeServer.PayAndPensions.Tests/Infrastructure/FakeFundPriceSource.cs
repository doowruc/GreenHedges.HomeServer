using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>
/// A price feed with made-up prices, per ISIN; an ISIN it hasn't got fails, as a real feed's 404 would. Its search
/// finds the funds added with <see cref="AddFund"/> whose name contains the query.
/// </summary>
public sealed class FakeFundPriceSource : IFundPriceSource
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<SourcePrice>> _prices = new();

    private readonly System.Collections.Concurrent.ConcurrentBag<FundMatch> _funds = [];

    public bool IsConfigured => true;

    public void AddFund(FundMatch fund) => _funds.Add(fund);

    public Task<IReadOnlyList<FundMatch>> SearchAsync(string query, CancellationToken cancellationToken) =>
        query.Contains("fails", StringComparison.OrdinalIgnoreCase)
            ? throw new HttpRequestException("Unavailable")
            : Task.FromResult<IReadOnlyList<FundMatch>>(_funds.Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());

    public void Set(string isin, params SourcePrice[] prices) => _prices[isin] = [.. prices];

    public Task<IReadOnlyList<SourcePrice>> GetAsync(string isin, DateOnly from, CancellationToken cancellationToken) =>
        _prices.TryGetValue(isin, out var prices)
            ? Task.FromResult<IReadOnlyList<SourcePrice>>(prices)
            : throw new HttpRequestException("Not found");
}
