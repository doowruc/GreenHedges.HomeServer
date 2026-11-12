using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;
using GreenHedges.HomeServer.Utilities.Services.Import;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;
using static GreenHedges.HomeServer.Utilities.Tests.Infrastructure.TestWorkbook;

namespace GreenHedges.HomeServer.Utilities.Tests.Unit;

/// <summary>Reading the meter readings sheet, and planning its import.</summary>
public class ReadingsImportTests
{
    private static readonly DateOnly Today = new(2022, 1, 1);

    private static WorkbookContents Read(TestWorkbook workbook) => ReadingsWorkbook.Read(new MemoryStream(workbook.Build()));

    private static SheetUtility Sheet(UtilityType utility, params (object? Date, object? Reading)[] rows) =>
        Read(new TestWorkbook().Block(utility.ToString(), rows)).Utilities.Single();

    [Fact]
    public void Each_utilitys_block_is_found_by_its_title_and_read_from_its_date_and_reading_columns()
    {
        var contents = Read(new TestWorkbook()
            .Block("Electricity", R(2021, 1, 1, 1000), R(2021, 1, 31, 1300.5))
            .Block("Gas", R(2021, 1, 1, 50.125))
            .Block("Water In", R(2020, 6, 1, 330), R(2020, 12, 1, 402), R(2021, 6, 1, 495)));

        Assert.Empty(contents.Problems);
        Assert.Equal([UtilityType.Electricity, UtilityType.Gas, UtilityType.Water], contents.Utilities.Select(u => u.Utility));
        Assert.Equal([(3, new DateOnly(2021, 1, 1), 1000m), (4, new DateOnly(2021, 1, 31), 1300.5m)],
            contents.Utilities[0].Readings.Select(r => (r.Row, r.Date, r.Value)));
        Assert.Equal(50.125m, contents.Utilities[1].Readings.Single().Value);
        Assert.Equal(3, contents.Utilities[2].Readings.Count);
    }

    [Fact]
    public void The_sheets_own_units_a_day_come_with_each_row_to_check_against()
    {
        var electricity = Sheet(UtilityType.Electricity, R(2021, 1, 1, 1000), R(2021, 1, 31, 1300));

        Assert.Equal([null, 10m], electricity.Readings.Select(r => r.SheetUnitsPerDay));
    }

    [Fact]
    public void A_utilitys_second_block_and_other_blocks_are_ignored()
    {
        var contents = Read(new TestWorkbook()
            .Block("Water In", R(2021, 1, 1, 330))
            .Block("Water In", R(2021, 1, 1, 999))        // the old waste water block
            .Block("Bill Date", R(2021, 1, 1, 12345)));   // one-off workings

        Assert.Equal(330m, contents.Utilities.Single().Readings.Single().Value);
    }

    [Fact]
    public void A_doubles_noise_is_rounded_away()
    {
        var gas = Sheet(UtilityType.Gas, R(2021, 1, 1, 35625.800000000003), R(2021, 2, 1, 326.72549999));

        Assert.Equal([35625.8m, 326.725m], gas.Readings.Select(r => r.Value));
    }

    [Fact]
    public void Rows_that_arent_a_date_and_a_number_are_skipped_with_the_reason_and_empty_ones_quietly()
    {
        var electricity = Sheet(UtilityType.Electricity,
            R(2021, 1, 1, 1000), (null, null), (null, 1100d), (new DateOnly(2021, 3, 1), null), ("G", null), (new DateOnly(2021, 4, 1), "E"),
            (new DateOnly(2021, 5, 1), -1d), R(2021, 6, 1, 1500));

        Assert.Equal([3, 10], electricity.Readings.Select(r => r.Row));
        Assert.Equal(
        [
            (5, "it has a reading but no date"), (6, "it has a date but no reading"), (7, "its date isn't a date"),
            (8, "its reading isn't a number"), (9, "its reading is out of range")
        ], electricity.Skipped);
    }

    [Fact]
    public void A_sheet_without_a_utilitys_headings_cant_be_imported()
    {
        var contents = Read(new TestWorkbook { Headings = ("When", "Value") }.Block("Electricity", R(2021, 1, 1, 1000)));

        Assert.Empty(contents.Utilities);
        Assert.Contains("No readings found", Assert.Single(contents.Problems));
    }

    [Fact]
    public void A_utility_with_no_meters_gets_its_first_and_a_replacement_wherever_the_reading_falls()
    {
        var sheet = Sheet(UtilityType.Electricity,
            R(2021, 2, 1, 5000), R(2021, 3, 1, 5280), R(2021, 3, 21, 150), R(2021, 3, 31, 240));

        var plan = ReadingsImporter.Plan(sheet, [], [], Today);

        Assert.Equal([new MeterInfo(-1, null, null), new MeterInfo(-2, new DateOnly(2021, 3, 21), 0m)], plan.NewMeters);
        Assert.Equal([-1, -1, -2, -2], plan.ToImport.Select(t => t.MeterId));
        // As the sheet: the new meter's first row is its reading less 0, over the days since the old meter's last.
        Assert.Equal([null, 10m, 7.5m, 9m], plan.Rows.Select(r => r.UnitsPerDay));
        Assert.Empty(plan.Mismatches);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Rows_out_of_order_are_sorted_and_a_date_twice_keeps_its_first_row()
    {
        var sheet = Sheet(UtilityType.Gas, R(2021, 2, 1, 120), R(2021, 1, 1, 100), R(2021, 2, 1, 125));

        var plan = ReadingsImporter.Plan(sheet, [], [], Today);

        Assert.Equal([4, 3], plan.ToImport.Select(t => t.Reading.Row));
        Assert.Equal((5, "the sheet has an earlier row for 01/02/2021"), Assert.Single(plan.Skipped));
    }

    [Fact]
    public void A_date_in_the_future_is_skipped()
    {
        var plan = ReadingsImporter.Plan(Sheet(UtilityType.Gas, R(2021, 1, 1, 100), R(2022, 1, 2, 120)), [], [], Today);

        Assert.Equal((4, "its date is in the future"), Assert.Single(plan.Skipped));
        Assert.Single(plan.ToImport);
    }

    [Fact]
    public void A_utility_with_readings_only_gains_the_dates_it_doesnt_have_on_the_meter_each_belongs_to()
    {
        MeterInfo[] meters = [new(1, null, null), new(2, new DateOnly(2021, 3, 10), 0m)];
        ReadingInfo[] readings = [new(1, 1, new(2021, 2, 1), 5000m), new(2, 1, new(2021, 3, 1), 5280m), new(3, 2, new(2021, 3, 21), 150m)];
        var sheet = Sheet(UtilityType.Electricity,
            R(2021, 2, 1, 5000), R(2021, 2, 15, 5140), R(2021, 3, 1, 5280), R(2021, 3, 21, 150), R(2021, 3, 31, 240));

        var plan = ReadingsImporter.Plan(sheet, meters, readings, Today);

        Assert.Empty(plan.NewMeters);
        Assert.Equal(3, plan.AlreadyThere);
        Assert.Equal([(new DateOnly(2021, 2, 15), 1), (new DateOnly(2021, 3, 31), 2)], plan.ToImport.Select(t => (t.Reading.Date, t.MeterId)));
        Assert.Equal(5, plan.Rows.Count);
        Assert.Equal([4, 7], plan.NewRows.Select(r => -r.Id));
        Assert.Empty(plan.Mismatches);
    }

    [Fact]
    public void A_reading_that_doesnt_fit_its_meter_is_skipped_not_made_a_new_meter()
    {
        MeterInfo[] meters = [new(1, null, null)];
        ReadingInfo[] readings = [new(1, 1, new(2021, 2, 1), 5000m), new(2, 1, new(2021, 3, 1), 5280m)];
        var sheet = Sheet(UtilityType.Electricity, R(2021, 2, 15, 5300), R(2021, 3, 21, 150), R(2021, 3, 31, 240));

        var plan = ReadingsImporter.Plan(sheet, meters, readings, Today);

        Assert.Empty(plan.NewMeters);
        Assert.Empty(plan.ToImport);
        Assert.Equal(
        [
            (3, "its reading is higher than the one after it"),
            (4, "its reading is lower than the one before it: if the meter was replaced, add the new meter first"),
            (5, "its reading is lower than the one before it: if the meter was replaced, add the new meter first")
        ], plan.Skipped);
    }

    [Fact]
    public void Units_a_day_that_differ_from_the_sheets_own_are_reported_by_row()
    {
        // The sheet's formula for row 5 counts from the row above; here that row is skipped (a future date can't
        // happen in a real sheet, but any skipped row has the same effect), so row 5 counts from row 3.
        var sheet = Sheet(UtilityType.Gas, R(2021, 1, 1, 100), R(2021, 1, 11, 110), R(2021, 1, 21, 130));
        var withoutMiddle = sheet with { Readings = [sheet.Readings[0], sheet.Readings[2]] };

        var plan = ReadingsImporter.Plan(withoutMiddle, [], [], Today);

        Assert.Equal([5], plan.Mismatches);   // 30 over 20 days here; 20 over 10 in the sheet
    }
}
