namespace GreenHedges.HomeServer.Presents.Services;

/// <summary>What a purchase counts as: its share of the cost, after the splits.</summary>
public static class Shares
{
    /// <summary>What the cost is divided by: 1, halved for each split (2 for one, 4 for both).</summary>
    public static int Divisor(bool splitWithMum, bool splitWithBirthday) =>
        (splitWithMum ? 2 : 1) * (splitWithBirthday ? 2 : 1);

    /// <summary>
    /// Price × quantity ÷ <see cref="Divisor"/>, rounded to the penny, half a penny up (e.g. £9.99 split two ways is
    /// £5.00). A year's spend is the sum of these rounded shares, so it's what the list shows added up.
    /// </summary>
    public static decimal Of(decimal price, int quantity, bool splitWithMum, bool splitWithBirthday) =>
        Math.Round(price * quantity / Divisor(splitWithMum, splitWithBirthday), 2, MidpointRounding.AwayFromZero);

    /// <summary>The splits in words, for the list: e.g. "Split with Mum and birthday"; null with none.</summary>
    public static string? Describe(bool splitWithMum, bool splitWithBirthday) => (splitWithMum, splitWithBirthday) switch
    {
        (true, true) => "Split with Mum and birthday",
        (true, false) => "Split with Mum",
        (false, true) => "Split with birthday",
        _ => null
    };
}
