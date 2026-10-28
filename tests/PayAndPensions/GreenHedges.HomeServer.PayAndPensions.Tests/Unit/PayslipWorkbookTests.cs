using ClosedXML.Excel;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services.Import;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

/// <summary>Made-up workbooks laid out like the real ones (<see cref="TestWorkbook"/>).</summary>
public class PayslipWorkbookTests
{
    [Fact]
    public void Kinds_come_from_the_Gross_and_Deductions_formulas()
    {
        var contents = PayslipWorkbook.Read(new TestWorkbook(["Salary", "Overtime"], ["Tax", "NI", "EE Pension"], ["ER Pension"])
            .Row(2027, 1, new DateOnly(2026, 4, 25), 3000, 120, 500, 200, 150, 225)
            .Build());

        Assert.Empty(contents.Problems);
        Assert.True(contents.KindsFromFormulas);
        Assert.Equal(
            ["Salary Earning Salary", "Overtime Earning Overtime", "Tax Deduction Tax", "NI Deduction NationalInsurance",
             "EE Pension Deduction Pension", "ER Pension EmployerContribution Pension"],
            contents.Columns.Select(c => $"{c.Name} {c.Kind} {c.Category}"));
    }

    [Fact]
    public void Each_row_is_a_payslip_in_the_tax_year_before_its_Year()
    {
        var row = PayslipWorkbook.Read(new TestWorkbook(["Salary"], ["Tax"])
            .Row(2027, 10, new DateOnly(2027, 1, 23), 3000.004, 500)
            .Build()).Rows.Single();

        Assert.Equal((2026, 10, new DateOnly(2027, 1, 23)), (row.TaxYear, row.TaxMonth, row.PayDate));
        Assert.Equal([3000m, 500m], row.Amounts.Values);   // to the penny
        Assert.Equal((3000m, 2500m), (row.SheetGross, row.SheetNet));
    }

    [Fact]
    public void Blank_and_zero_amounts_arent_lines_and_typed_sums_are_their_values()
    {
        var row = PayslipWorkbook.Read(new TestWorkbook(["Salary", "WFH", "Back Pay"], ["Tax"])
            .Row(2027, 1, new DateOnly(2026, 4, 25), 3000, "=26+74", null, 0)
            .Build()).Rows.Single();

        Assert.Equal(new Dictionary<int, decimal> { [0] = 3000m, [1] = 100m }, row.Amounts);
    }

    [Fact]
    public void Months_laid_out_but_not_paid_yet_are_left_out()
    {
        var contents = PayslipWorkbook.Read(new TestWorkbook(["Salary"], ["Tax"])
            .Row(2027, 1, new DateOnly(2026, 4, 25), 3000, 500)
            .Row(2027, 2, null)
            .Row(2027, 3, null)
            .Build());

        Assert.Single(contents.Rows);
        Assert.Empty(contents.SkippedRows);
    }

    [Fact]
    public void A_date_outside_its_tax_year_is_skipped_with_the_reason()
    {
        var contents = PayslipWorkbook.Read(new TestWorkbook(["Salary"], ["Tax"])
            .Row(2018, 2, new DateOnly(2017, 5, 25), 3000, 500)
            .Row(2018, 3, new DateOnly(2003, 6, 27), 3000, 500)   // a typed-in year that's wrong
            .Build());

        Assert.Single(contents.Rows);
        Assert.Equal((3, "the date 27/06/2003 isn't in 2017-18"), contents.SkippedRows.Single());
    }

    [Fact]
    public void A_heading_used_twice_gets_its_kind_added()
    {
        var contents = PayslipWorkbook.Read(new TestWorkbook(["Salary", "Adj"], ["Tax", "Adj"])
            .Row(2027, 1, new DateOnly(2026, 4, 25), 3000, 10, 500, 5)
            .Build());

        Assert.Equal(["Salary", "Adj", "Tax", "Adj (deduction)"], contents.Columns.Select(c => c.Name));
    }

    [Fact]
    public void Pension_received_dates_and_text_notes_come_too_but_not_worked_out_notes()
    {
        var contents = PayslipWorkbook.Read(new TestWorkbook(["Salary"], ["Tax", "EE Pension"], ["ER Pension"])
            {
                BeforeGross = ["Transfer Date"], AfterNet = ["Notes"]
            }
            .Row(2027, 2, new DateOnly(2026, 5, 25), 3000, 500, 150, 225, new DateOnly(2026, 7, 9), "Commission")
            .Row(2027, 3, new DateOnly(2026, 6, 25), 3000, 500, 150, 225, null, "=D3/12")
            .Build());

        Assert.Equal(4, contents.Columns.Count);   // Transfer Date isn't a pay column
        Assert.Equal([new DateOnly(2026, 7, 9), null], contents.Rows.Select(r => r.PensionReceivedDate));
        Assert.Equal(["Commission", null], contents.Rows.Select(r => r.Notes));
    }

    [Fact]
    public void Without_the_formulas_columns_from_Tax_on_are_deductions()
    {
        var contents = PayslipWorkbook.Read(new TestWorkbook(["Salary", "Bonus"], ["Tax", "NI"]) { NoFormulas = true }
            .Row(2027, 1, new DateOnly(2026, 4, 25), 3000, 100, 500, 200)
            .Build());

        Assert.False(contents.KindsFromFormulas);
        Assert.Equal([PayElementKind.Earning, PayElementKind.Earning, PayElementKind.Deduction, PayElementKind.Deduction],
            contents.Columns.Select(c => c.Kind));
    }

    [Fact]
    public void A_sheet_without_the_columns_it_needs_says_what_is_missing()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell(1, 1).Value = "Year";
        sheet.Cell(1, 2).Value = "Date";
        sheet.Cell(1, 3).Value = "Salary";

        Assert.Equal(["No Period (tax month) column."], PayslipWorkbook.Read(sheet).Problems);
        Assert.Equal("No heading row with Year and Date in the first 10 rows of the first sheet.",
            PayslipWorkbook.Read(new XLWorkbook().AddWorksheet("Empty")).Problems.Single());
    }

    [Theory]
    [InlineData("Salary", PayElementKind.Earning, PayCategory.Salary)]
    [InlineData("Annual Bonus", PayElementKind.Earning, PayCategory.Bonus)]
    [InlineData("Xmas", PayElementKind.Earning, PayCategory.Bonus)]
    [InlineData("O/T", PayElementKind.Earning, PayCategory.Overtime)]
    [InlineData("Back Pay", PayElementKind.Earning, PayCategory.BackPay)]
    [InlineData("WFH", PayElementKind.Earning, PayCategory.Allowance)]
    [InlineData("Adj", PayElementKind.Earning, PayCategory.Other)]
    [InlineData("Tax", PayElementKind.Deduction, PayCategory.Tax)]
    [InlineData("NI", PayElementKind.Deduction, PayCategory.NationalInsurance)]
    [InlineData("EE Pension", PayElementKind.Deduction, PayCategory.Pension)]
    [InlineData("Flex", PayElementKind.Deduction, PayCategory.Other)]
    [InlineData("ER Pension", PayElementKind.EmployerContribution, PayCategory.Pension)]
    public void Categories_are_guessed_from_headings(string heading, PayElementKind kind, PayCategory category) =>
        Assert.Equal(category, PayslipWorkbook.GuessCategory(heading, kind));
}
