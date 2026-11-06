using GreenHedges.HomeServer.PayAndPensions.Services.Import;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

/// <summary>Made-up statements, pasted as Excel copies them: tab-separated, a heading row first.</summary>
public class StatementPasteTests
{
    private static string Paste(params string[] rows) => string.Join("\r\n", rows.Select(r => r.Replace('|', '\t'))) + "\r\n";

    [Fact]
    public void Rows_are_read_by_their_headings_and_other_columns_ignored()
    {
        var result = StatementPaste.Parse(Paste(
            "Date|When|Value|Change|Percentage",
            "15/03/2015||0||",
            "01/06/2015|Spring 2015|1,250|1250|",
            "01/12/2015|Autumn 2015|£1,400.55|150.55|0.1"), hasProjections: false);

        Assert.Null(result.Error);
        Assert.Equal([new PastedStatement(3, new DateOnly(2015, 6, 1), "Spring 2015", 1250m, null, null),
                      new PastedStatement(4, new DateOnly(2015, 12, 1), "Autumn 2015", 1400.55m, null, null)], result.Rows);
        Assert.Equal(["Change", "Percentage"], result.Ignored);
        Assert.Equal(new DateOnly(2015, 3, 15), result.StartDate);   // the 0 first row
        Assert.Equal(["Row 2 (15/03/2015): a value of 0, taken as the start."], result.Skipped);
    }

    [Fact]
    public void Projections_come_from_their_columns_wherever_they_are()
    {
        var text = Paste(
            "Date|Value|Change|Retirement|Annual|Monthly",
            "01/09/2016|2000|2000|150,000|4,500|375",
            "01/09/2017|2500|500||4,800|400");

        var result = StatementPaste.Parse(text, hasProjections: true);
        var without = StatementPaste.Parse(text, hasProjections: false);

        Assert.Equal([(150000m, 4500m), (null, 4800m)], result.Rows.Select(r => (r.ProjectedFund, r.ProjectedAnnualIncome)));
        Assert.Equal(["Change", "Monthly"], result.Ignored);
        Assert.All(without.Rows, r => Assert.Null(r.ProjectedFund));
        Assert.Equal(["Change", "Retirement", "Annual", "Monthly"], without.Ignored);
    }

    [Theory]
    [InlineData("01/06/2015")]
    [InlineData("1/6/15")]
    [InlineData("01-Jun-15")]
    [InlineData("1 June 2015")]
    [InlineData("2015-06-01")]
    [InlineData("42156")]   // Excel's serial number
    public void Dates_are_read_as_Excel_shows_them(string date)
    {
        var result = StatementPaste.Parse(Paste("Date|Value", $"{date}|100"), hasProjections: false);

        Assert.Equal(new DateOnly(2015, 6, 1), Assert.Single(result.Rows).Date);
    }

    [Fact]
    public void Rows_that_cant_be_read_are_skipped_with_the_reason_and_blank_ones_quietly()
    {
        var result = StatementPaste.Parse(Paste(
            "Date|Value",
            "01/06/2015|100",
            "|",
            "soon|200",
            "01/07/2015|",
            "01/08/2015|lots",
            "01/06/2015|300",
            "01/09/2015|(50)"), hasProjections: false);

        Assert.Equal([100m, -50m], result.Rows.Select(r => r.Value));
        Assert.Equal(["Row 4: \"soon\" isn't a date.", "Row 5 (01/07/2015): no value.",
                      "Row 6 (01/08/2015): \"lots\" isn't an amount.", "Row 7 (01/06/2015): the same date as row 2."], result.Skipped);
        Assert.Null(result.StartDate);
    }

    [Fact]
    public void A_0_after_other_rows_isnt_the_start()
    {
        var result = StatementPaste.Parse(Paste("Date|Value", "01/06/2015|100", "01/07/2015|0"), hasProjections: false);

        Assert.Null(result.StartDate);
        Assert.Equal(["Row 3 (01/07/2015): a value of 0."], result.Skipped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01/06/2015\t100")]
    [InlineData("Date\tChange\n01/06/2015\t100")]
    public void Without_Date_and_Value_headings_nothing_is_read(string text)
    {
        var result = StatementPaste.Parse(text, hasProjections: false);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Rows_above_the_heading_row_are_ignored()
    {
        var result = StatementPaste.Parse(Paste("Investment Report||", "Date|When|Value", "01/06/2015|Spring 2015|100"), hasProjections: false);

        Assert.Null(result.Error);
        Assert.Equal(3, Assert.Single(result.Rows).Row);
    }
}
