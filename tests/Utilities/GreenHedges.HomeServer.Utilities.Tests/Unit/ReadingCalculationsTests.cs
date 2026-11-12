using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;

namespace GreenHedges.HomeServer.Utilities.Tests.Unit;

public class ReadingCalculationsTests
{
    private static readonly MeterInfo First = new(1, null, null);
    private static readonly MeterInfo Replacement = new(2, new DateOnly(2021, 3, 10), 0m);

    private static DateOnly D(int month, int day) => new(2021, month, day);

    [Fact]
    public void Units_are_the_reading_less_the_one_before_and_days_the_days_between()
    {
        var rows = ReadingCalculations.Calculate([First],
        [
            new(1, 1, D(1, 1), 1000m), new(2, 1, D(1, 31), 1300m), new(3, 1, D(2, 10), 1350.5m)
        ]);

        Assert.Equal([(300m, 30, 10m), (50.5m, 10, 5.05m)], rows.Skip(1).Select(r => (r.Units!.Value, r.Days!.Value, r.UnitsPerDay!.Value)));
    }

    [Fact]
    public void A_utilitys_first_reading_is_only_where_counting_starts()
    {
        var row = Assert.Single(ReadingCalculations.Calculate([First], [new(1, 1, D(1, 1), 1000m)]));

        Assert.Equal((null, null, null, false), (row.Units, row.Days, row.UnitsPerDay, row.NewMeter));
    }

    [Fact]
    public void Readings_come_back_oldest_first_whatever_order_they_were_given_in()
    {
        var rows = ReadingCalculations.Calculate([First], [new(2, 1, D(2, 1), 1310m), new(1, 1, D(1, 1), 1000m)]);

        Assert.Equal([1, 2], rows.Select(r => r.Id));
        Assert.Equal(310m, rows[1].Units);
    }

    [Fact]
    public void A_replacement_meters_first_reading_counts_units_from_its_start_and_days_from_the_old_meters_last()
    {
        // As the spreadsheet: the new meter's first row is "reading - 0", over the days since the old meter's last.
        var rows = ReadingCalculations.Calculate([First, Replacement],
        [
            new(1, 1, D(2, 1), 5000m), new(2, 1, D(3, 1), 5280m), new(3, 2, D(3, 21), 150m), new(4, 2, D(3, 31), 240m)
        ]);

        Assert.Equal((150m, 20, 7.5m, true), (rows[2].Units, rows[2].Days, rows[2].UnitsPerDay, rows[2].NewMeter));
        Assert.Equal((90m, 10, 9m, false), (rows[3].Units, rows[3].Days, rows[3].UnitsPerDay, rows[3].NewMeter));
    }

    [Fact]
    public void A_replacement_can_start_above_zero()
    {
        var rows = ReadingCalculations.Calculate([First, Replacement with { StartReading = 12.5m }],
            [new(1, 1, D(3, 1), 5280m), new(2, 2, D(3, 11), 20m)]);

        Assert.Equal(7.5m, rows[1].Units);
    }

    [Fact]
    public void A_replacement_with_nothing_before_it_has_units_but_no_days()
    {
        var row = Assert.Single(ReadingCalculations.Calculate([Replacement], [new(1, 2, D(3, 21), 150m)]));

        Assert.Equal((150m, null, null, true), (row.Units, row.Days, row.UnitsPerDay, row.NewMeter));
    }

    [Fact]
    public void Units_a_day_keep_their_precision()
    {
        var rows = ReadingCalculations.Calculate([First], [new(1, 1, D(1, 1), 0m), new(2, 1, D(1, 4), 1m)]);

        Assert.Equal(1m / 3, rows[1].UnitsPerDay);
    }

    [Theory]
    [InlineData(3, 9, 1)]    // the day before the replacement: still the old meter
    [InlineData(3, 10, 2)]   // from its installed date on: the replacement
    [InlineData(12, 31, 2)]
    public void A_reading_belongs_to_the_latest_meter_installed_on_or_before_its_date(int month, int day, int meterId) =>
        Assert.Equal(meterId, ReadingCalculations.MeterFor([Replacement, First], D(month, day))!.Id);

    [Fact]
    public void A_date_before_every_meter_has_none() =>
        Assert.Null(ReadingCalculations.MeterFor([Replacement], D(3, 9)));

    [Theory]
    [InlineData(UtilityType.Electricity, MeterUnit.KilowattHours, "kWh", "12.35")]
    [InlineData(UtilityType.Gas, MeterUnit.CubicMetres, "m³", "12.35")]
    [InlineData(UtilityType.Water, MeterUnit.CubicMetres, "litres", "12,346")]
    public void The_daily_figure_is_in_the_meters_unit_except_water_in_litres(UtilityType utility, MeterUnit unit, string label, string shown)
    {
        Assert.Equal(unit, utility.DefaultUnit());
        Assert.Equal(label, utility.PerDayUnit(unit));
        Assert.Equal(shown, utility.FormatPerDay(unit, 12.3456m));
        Assert.Equal("", utility.FormatPerDay(unit, null));
    }

    [Theory]
    [InlineData("1234.5", "1,234.5")]
    [InlineData("326.725", "326.725")]
    [InlineData("80", "80")]
    public void Readings_show_up_to_three_decimal_places_unpadded(string value, string shown) =>
        Assert.Equal(shown, UtilityUnits.FormatUnits(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("Electricity", true)]
    [InlineData("gas", true)]
    [InlineData("WATER", true)]
    [InlineData("Oil", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    public void A_utility_in_an_address_is_its_name_in_any_case(string text, bool valid) =>
        Assert.Equal(valid, UtilityRouteConstraint.TryParse(text, out _));
}
