using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services.Import;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

/// <summary>Made-up transaction exports (<see cref="TestCsv"/>).</summary>
public class TransactionCsvTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lines_are_read_after_the_account_lines_in_either_encoding(bool utf8)
    {
        var result = TransactionCsv.Read(TestCsv.Export(
        [
            TestCsv.Purchase("28/02/2020", "B100", "1,234.5", "250.123400", "-3,087.77"),
            TestCsv.Contribution("21/02/2020", "1,000.00"),
        ], utf8));

        Assert.Null(result.Error);
        Assert.Empty(result.Skipped);
        Assert.Equal(
        [
            new CsvTransaction(7, new DateOnly(2020, 2, 28), new DateOnly(2020, 2, 28), "B100", $"{TestCsv.Fund} 1,234.5 @  250.123400",
                TransactionKind.Purchase, TestCsv.Fund, 250.1234m, 1234.5m, -3087.77m),
            new CsvTransaction(8, new DateOnly(2020, 2, 21), new DateOnly(2020, 2, 21), "FPC123", "SIPP Contribution",
                TransactionKind.Contribution, null, null, null, 1000m),
        ], result.Rows);
    }

    [Fact]
    public void A_file_without_the_heading_row_is_an_error()
    {
        var result = TransactionCsv.Read("Date,Amount\r\n01/01/2020,5\r\n"u8.ToArray());

        Assert.Equal("It has no heading row with Trade date and Value: is it a transaction history export?", result.Error);
        Assert.Equal("The file is empty.", TransactionCsv.Read([]).Error);
    }

    [Fact]
    public void Unreadable_lines_are_skipped_with_the_reason()
    {
        var result = TransactionCsv.Read(TestCsv.Export(
            TestCsv.Line("31/02/2020", "X", "Something", "n/a", "n/a", "1.00"),
            TestCsv.Line("01/03/2020", "X", "Something", "n/a", "n/a", "lots"),
            TestCsv.Line("01/03/2020", "X", "", "n/a", "n/a", "1.00"),
            TestCsv.Interest("09/03/2020", "2.50")));

        Assert.Equal(["Line 7: the trade date \"31/02/2020\" isn't a date.", "Line 8: the value \"lots\" isn't an amount.",
                      "Line 9: it has no description."], result.Skipped);
        Assert.Equal(TransactionKind.Interest, Assert.Single(result.Rows).Kind);
    }

    [Theory]
    [InlineData("FPC1", "SIPP Contribution", null, 100, TransactionKind.Contribution)]
    [InlineData("ABC", "Employer contribution", null, 100, TransactionKind.Contribution)]
    [InlineData("BACS", "Sipp Transfer (BACS)", null, 5000, TransactionKind.TransferIn)]
    [InlineData("BACS", "Transfer out", null, -5000, TransactionKind.Other)]
    [InlineData("INTEREST", "Interest From 01/01/2020 TO 31/01/2020", null, 2, TransactionKind.Interest)]
    [InlineData("MANAGE FEE", "Example SIPP To A/C 000", null, -10, TransactionKind.Fee)]
    [InlineData("URI1", "Unit Rebate Re-Investment Example Fund", null, 3, TransactionKind.Rebate)]
    [InlineData("B1", "Example Fund 10 @ 100", 10, -10, TransactionKind.Purchase)]
    [InlineData("S1", "Example Fund 10 @ 100", 10, 10, TransactionKind.Sale)]
    [InlineData("S2", "Example Fund Fee Sale - 10 @ 100", 10, 10, TransactionKind.Sale)]          // units sold to pay a fee
    [InlineData("B2", "Example Contribution Rebate Fund 10 @ 100", 10, -10, TransactionKind.Purchase)]
    [InlineData("Z", "Something else", null, 1, TransactionKind.Other)]
    public void Kinds_are_worked_out_from_the_reference_and_description(string reference, string description, int? quantity, int value, TransactionKind kind) =>
        Assert.Equal(kind, TransactionCsv.Kind(reference, description, quantity, value));

    [Theory]
    [InlineData("Example Fund Class I - Accumulation (GBP) 459.749 @  399.472300", "Example Fund Class I - Accumulation (GBP)")]
    [InlineData("Example Fund 17,667.688 @ 401.2238", "Example Fund")]
    [InlineData("Example Fund Class I - Accumulation (GBP) Fee Sale - 1.234 @  399.472300", "Example Fund Class I - Accumulation (GBP)")]
    [InlineData("SIPP Contribution", null)]
    [InlineData("Interest From 01/01/2020 TO 31/01/2020", null)]
    public void The_fund_is_the_description_before_its_units_and_price(string description, string? fund) =>
        Assert.Equal(fund, TransactionCsv.Fund(description));

    [Fact]
    public void New_rows_leave_out_those_already_there_and_count_overlapping_files_once()
    {
        var older = TransactionCsv.Read(TestCsv.Export(
            TestCsv.Contribution("21/01/2020", "100.00"),
            TestCsv.Interest("09/02/2020", "1.00"),
            TestCsv.Interest("09/02/2020", "1.00"))).Rows;          // two identical lines: both count
        var newer = TransactionCsv.Read(TestCsv.Export(
            TestCsv.Interest("09/02/2020", "1.00"),               // overlaps the older file
            TestCsv.Contribution("21/03/2020", "100.00"))).Rows;
        var existing = new[] { older[0].Key, older[1].Key };      // the contribution and one of the interest lines

        var added = TransactionCsv.NewRows([older, newer], existing);

        Assert.Equal([(new DateOnly(2020, 2, 9), TransactionKind.Interest), (new DateOnly(2020, 3, 21), TransactionKind.Contribution)],
            added.Select(r => (r.TradeDate, r.Kind)));
        Assert.Empty(TransactionCsv.NewRows([older, newer], [.. older.Concat(newer).Select(r => r.Key)]));
    }
}
