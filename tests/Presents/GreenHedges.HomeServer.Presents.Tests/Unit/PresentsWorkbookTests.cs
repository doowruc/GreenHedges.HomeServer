using GreenHedges.HomeServer.Presents.Services.Import;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;
using static GreenHedges.HomeServer.Presents.Tests.Infrastructure.TestWorkbook;

namespace GreenHedges.HomeServer.Presents.Tests.Unit;

public class PresentsWorkbookTests
{
    private static WorkbookContents Read(TestWorkbook workbook)
    {
        using var stream = new MemoryStream(workbook.Build());
        return PresentsWorkbook.Read(stream);
    }

    [Fact]
    public void Each_year_sheet_gives_its_presents_with_formulas_worked_out_and_the_rest_of_the_sheet_ignored()
    {
        var contents = Read(new TestWorkbook().Sheet("2023",
            R(2023, 8, 19, "Tech Shop", "Tablet", "Bea", "=780/4"),
            R(2023, 11, 2, "Sock Shop", "Socks", "Ash", "=4.99*2"),
            new Row("Unknown", null, "  Chocolate  bar ", " Ash ", 2.5)));

        var year = Assert.Single(contents.Years);
        Assert.Empty(contents.Problems);
        Assert.Equal(2023, year.Year);
        Assert.Equal([
            new SheetPurchase(2, new DateOnly(2023, 8, 19), "Tech Shop", "Tablet", "Bea", 195m),
            new SheetPurchase(3, new DateOnly(2023, 11, 2), "Sock Shop", "Socks", "Ash", 9.98m),
            new SheetPurchase(4, null, null, "Chocolate bar", "Ash", 2.5m)], year.Purchases);
        Assert.Empty(year.Cash);
        Assert.Empty(year.Skipped);
    }

    [Fact]
    public void Cash_rows_are_kept_apart()
    {
        var year = Assert.Single(Read(new TestWorkbook().Sheet("2024",
            R(2024, 12, 1, "Toy Shop", "Jigsaw", "Ash", 10),
            R(2024, 12, 25, null, "Cash", "Bea", 12.34),
            R(2024, 12, 25, "Bank", "cash", "Ash", "=1+2.5"))).Years);

        Assert.Equal([new SheetCash(3, "Bea", 12.34m), new SheetCash(4, "Ash", 3.5m)], year.Cash);
        Assert.Single(year.Purchases);
    }

    [Fact]
    public void Rows_that_cant_be_read_are_skipped_with_why_and_blank_ones_ignored()
    {
        var year = Assert.Single(Read(new TestWorkbook().Sheet("2024",
            R(2024, 12, 1, "Shop", null, "Ash", 10),
            R(2024, 12, 1, "Shop", "Jigsaw", null, 10),
            R(2024, 12, 1, "Shop", "Jigsaw", "Ash", null),
            R(2024, 12, 1, "Shop", "Jigsaw", "Ash", "ten"),
            R(2024, 12, 1, "Shop", "Jigsaw", "Ash", 0),
            R(2024, 12, 1, "Shop", "Jigsaw", "Ash", -5),
            new Row(null, null, null, null, null),
            R(2024, 12, 1, "Shop", "Jigsaw", "Ash", 10))).Years);

        Assert.Equal([
            (2, "it has no item"), (3, "it doesn't say who it's for"), (4, "it has no amount"), (5, "its amount isn't a number"),
            (6, "its amount isn't more than £0"), (7, "its amount isn't more than £0")], year.Skipped);
        Assert.Equal(9, Assert.Single(year.Purchases).Row);
    }

    [Fact]
    public void Headings_can_be_a_few_rows_down_and_amounts_are_rounded_to_the_penny()
    {
        var year = Assert.Single(Read(new TestWorkbook().Sheet("2022", 3, false, R(2022, 12, 1, "Shop", "Puzzle", "Ash", "=10/3"))).Years);

        Assert.Equal((4, 3.33m), (year.Purchases[0].Row, year.Purchases[0].Amount));
    }

    [Fact]
    public void Only_sheets_named_by_a_year_count_oldest_first_and_one_without_headings_is_a_warning()
    {
        var contents = Read(new TestWorkbook()
            .Sheet("2025", R(2025, 12, 1, "Shop", "Puzzle", "Ash", 5))
            .Sheet("Notes", R(2025, 12, 1, "Shop", "Puzzle", "Ash", 5))
            .Sheet("2019", R(2019, 12, 1, "Shop", "Puzzle", "Ash", 5))
            .Sheet("2020", 12, false, R(2020, 12, 1, "Shop", "Puzzle", "Ash", 5)));   // headings too far down

        Assert.Equal([2019, 2025], contents.Years.Select(y => y.Year));
        Assert.Empty(contents.Problems);
        Assert.Contains("The 2020 sheet has no headings", Assert.Single(contents.Warnings));
    }

    [Fact]
    public void A_workbook_without_year_sheets_is_a_problem()
    {
        var contents = Read(new TestWorkbook().Sheet("Sheet1", R(2025, 12, 1, "Shop", "Puzzle", "Ash", 5)));

        Assert.Empty(contents.Years);
        Assert.StartsWith("No years found", Assert.Single(contents.Problems));
    }
}
