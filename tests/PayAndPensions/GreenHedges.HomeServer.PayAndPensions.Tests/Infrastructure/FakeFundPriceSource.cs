using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>A price feed with made-up prices, per ISIN; an ISIN it hasn't got fails, as a real feed's 404 would.</summary>
public sealed class FakeFundPriceSource : IFundPriceSource
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<SourcePrice>> _prices = new();

    public bool IsConfigured => true;

    public void Set(string isin, params SourcePrice[] prices) => _prices[isin] = [.. prices];

    public Task<IReadOnlyList<SourcePrice>> GetAsync(string isin, DateOnly from, CancellationToken cancellationToken) =>
        _prices.TryGetValue(isin, out var prices)
            ? Task.FromResult<IReadOnlyList<SourcePrice>>(prices)
            : throw new HttpRequestException("Not found");
}
