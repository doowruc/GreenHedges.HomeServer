using GreenHedges.HomeServer.Presents.Services;

namespace GreenHedges.HomeServer.Presents.Tests.Unit;

public class SharesTests
{
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 4)]
    public void Each_split_halves_it(bool mum, bool birthday, int divisor) =>
        Assert.Equal(divisor, Shares.Divisor(mum, birthday));

    [Theory]
    [InlineData("640", 1, true, true, "160")]
    [InlineData("32.40", 1, true, true, "8.10")]
    [InlineData("100", 1, true, false, "50")]
    [InlineData("100", 1, false, true, "50")]
    [InlineData("9.99", 2, false, false, "19.98")]
    [InlineData("2.85", 3, false, false, "8.55")]
    [InlineData("12.34", 3, true, true, "9.26")]     // 37.02 ÷ 4 = 9.255: half a penny up
    [InlineData("9.99", 1, true, false, "5.00")]     // 4.995: half a penny up
    [InlineData("0.01", 1, true, true, "0.00")]      // a quarter of a penny rounds down
    public void A_share_is_price_times_quantity_over_the_splits_to_the_penny(string price, int quantity, bool mum, bool birthday, string share) =>
        Assert.Equal(decimal.Parse(share), Shares.Of(decimal.Parse(price), quantity, mum, birthday));

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, "Split with Mum")]
    [InlineData(false, true, "Split with birthday")]
    [InlineData(true, true, "Split with Mum and birthday")]
    public void Splits_are_described_for_the_list(bool mum, bool birthday, string? text) =>
        Assert.Equal(text, Shares.Describe(mum, birthday));
}
