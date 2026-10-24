using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries.Tests;

public sealed class MinuteSummariserTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private long _nextId = 1;

    [Fact]
    public void A_complete_minute_averages_temperature_humidity_wind_and_signal_and_takes_extremes()
    {
        var readings = new[]
        {
            Temperature(0, 10.0, humidity: 80, wind: 1.0, gust: 2.0, dir: 90, rssi: -1, snr: 20),
            Rain(12, 500.0, wind: 2.0, gust: 3.5, dir: 90, rssi: -2, snr: 22),
            Temperature(24, 11.0, humidity: 84, wind: 3.0, gust: 4.0, dir: 90, rssi: -3, snr: 24),
        };

        var minute = Assert.Single(MinuteSummariser.Summarise(readings, null, 499.6).Minutes);

        Assert.Equal(T0.UtcDateTime, minute.MinuteUtc);
        Assert.Equal(DateTimeKind.Utc, minute.MinuteUtc.Kind);
        Assert.Equal(3, minute.Messages);
        Assert.Equal(10.5, minute.TemperatureAvgC);
        Assert.Equal(10.0, minute.TemperatureMinC);
        Assert.Equal(11.0, minute.TemperatureMaxC);
        Assert.Equal(82, minute.HumidityAvg);
        Assert.Equal(2.0, minute.WindAvgMs);
        Assert.Equal(4.0, minute.GustMaxMs);
        Assert.Equal(90, minute.WindDirDeg);
        Assert.Equal(0.4, minute.RainMm);
        Assert.Equal(-2, minute.RssiAvgDb);
        Assert.Equal(22, minute.SnrAvgDb);
    }

    [Fact]
    public void Readings_are_split_into_utc_minutes()
    {
        var readings = new[] { Temperature(0, 10), Temperature(59.9, 11), Temperature(60, 12), Temperature(185, 13) };

        var minutes = MinuteSummariser.Summarise(readings, null, null).Minutes;

        Assert.Equal(
            [T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1), T0.UtcDateTime.AddMinutes(3)],
            minutes.Select(m => m.MinuteUtc));
        Assert.Equal([2, 1, 1], minutes.Select(m => m.Messages));
    }

    [Fact]
    public void Wind_direction_is_a_vector_mean_so_north_west_and_north_east_make_north()
    {
        var readings = new[] { Temperature(0, 10, wind: 2, dir: 315), Temperature(12, 10, wind: 2, dir: 45) };

        Assert.Equal(0, Assert.Single(MinuteSummariser.Summarise(readings, null, null).Minutes).WindDirDeg);
    }

    [Fact]
    public void Wind_direction_is_weighted_by_speed()
    {
        // 3 m/s from the east and 1 m/s from the north: atan2(3, 1) = 71.6°.
        var readings = new[] { Temperature(0, 10, wind: 3, dir: 90), Temperature(12, 10, wind: 1, dir: 0) };

        Assert.Equal(72, Assert.Single(MinuteSummariser.Summarise(readings, null, null).Minutes).WindDirDeg);
    }

    [Fact]
    public void A_calm_minute_has_no_wind_direction()
    {
        var readings = new[] { Temperature(0, 10, wind: 0, dir: 112), Temperature(12, 10, wind: 0, dir: 270) };

        var minute = Assert.Single(MinuteSummariser.Summarise(readings, null, null).Minutes);

        Assert.Null(minute.WindDirDeg);
        Assert.Equal(0, minute.WindAvgMs);
    }

    [Fact]
    public void Rain_is_the_change_in_the_total_across_minutes()
    {
        var readings = new[] { Rain(0, 999.6), Rain(24, 1000.0), Rain(60, 1000.0), Rain(84, 1005.6) };

        var minutes = MinuteSummariser.Summarise(readings, null, 999.6).Minutes;

        Assert.Equal([0.4, 5.6], minutes.Select(m => m.RainMm));
    }

    [Fact]
    public void Without_an_earlier_total_the_first_one_is_only_a_baseline()
    {
        var readings = new[] { Rain(0, 998.4), Rain(24, 998.8) };

        Assert.Equal(0.4, Assert.Single(MinuteSummariser.Summarise(readings, null, null).Minutes).RainMm);
    }

    [Fact]
    public void A_minute_without_a_rain_message_has_no_rain_value_and_no_temperature_without_one()
    {
        var minutes = MinuteSummariser.Summarise([Temperature(0, 10), Rain(60, 5)], null, 5).Minutes;

        Assert.Null(minutes[0].RainMm);
        Assert.Null(minutes[1].TemperatureAvgC);
        Assert.Null(minutes[1].BatteryOk);
        Assert.Equal(0, minutes[1].RainMm);
    }

    [Fact]
    public void A_total_lower_than_the_last_is_a_reset_and_counts_in_full()
    {
        var readings = new[] { Rain(0, 1012.4), Rain(24, 0.0), Rain(60, 0.8) };

        var result = MinuteSummariser.Summarise(readings, null, 1012.0);

        Assert.Equal([0.4, 0.8], result.Minutes.Select(m => m.RainMm));
        var reset = Assert.Single(result.Resets);
        Assert.Equal((readings[1].MessageId, 1012.4, 0.0), (reset.MessageId, reset.FromMm, reset.ToMm));
    }

    [Fact]
    public void A_gap_puts_all_the_rain_since_the_last_total_in_the_first_minute_after_it()
    {
        // The collector was down for a day; 6 mm fell meanwhile.
        var minutes = MinuteSummariser.Summarise([Rain(86_400, 506.0)], null, 500.0).Minutes;

        Assert.Equal(6.0, Assert.Single(minutes).RainMm);
    }

    [Fact]
    public void A_repeat_of_the_previous_message_within_two_seconds_is_skipped_even_with_a_different_signal()
    {
        var first = Rain(0, 500.4, rssi: -1, snr: 20);
        var repeat = Rain(1.5, 500.4, rssi: -4, snr: 18);
        var later = Rain(14, 500.4, rssi: -1, snr: 20); // identical, but 12 s later: a real message

        var minute = Assert.Single(MinuteSummariser.Summarise([first, repeat, later], null, 500.0).Minutes);

        Assert.Equal(2, minute.Messages);
        Assert.Equal(0.4, minute.RainMm);
        Assert.Equal(-1, minute.RssiAvgDb);
    }

    [Fact]
    public void A_repeat_of_the_reading_before_the_batch_is_skipped()
    {
        var previous = Rain(0, 500.4);

        Assert.Empty(MinuteSummariser.Summarise([Rain(1, 500.4)], previous, 500.4).Minutes);
    }

    [Fact]
    public void A_minute_with_any_low_battery_message_is_low()
    {
        var readings = new[] { Temperature(0, 10, battery: 1), Temperature(24, 10, battery: 0) };

        Assert.False(Assert.Single(MinuteSummariser.Summarise(readings, null, null).Minutes).BatteryOk);
        Assert.True(Assert.Single(MinuteSummariser.Summarise([Temperature(0, 10, battery: 1)], null, null).Minutes).BatteryOk);
    }

    [Fact]
    public void Rain_float_noise_is_rounded_to_the_sensors_tenths()
    {
        var minute = Assert.Single(MinuteSummariser.Summarise([Rain(0, 1000.0)], null, 999.6).Minutes);

        Assert.Equal(0.4, minute.RainMm); // 1000.0 - 999.6 = 0.40000000000009095
    }

    private StationReading Temperature(
        double seconds, double temperature, double humidity = 85, double wind = 1, double gust = 2, double dir = 90,
        int battery = 1, double rssi = -1, double snr = 20) =>
        Reading(seconds, new WeatherFields(temperature, humidity, wind, gust, dir, null, battery, 0, 1, rssi, snr));

    private StationReading Rain(
        double seconds, double total, double wind = 1, double gust = 2, double dir = 90, double rssi = -1, double snr = 20) =>
        Reading(seconds, new WeatherFields(null, null, wind, gust, dir, total, null, 1, 1, rssi, snr));

    private StationReading Reading(double seconds, WeatherFields weather) =>
        new(_nextId++, T0.AddSeconds(seconds), "12345678", weather);
}
