using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class RemindersTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static DueDateView Due(int daysLeft, DueDateKind kind = DueDateKind.Mot, int vehicleId = 1) =>
        new(vehicleId, "Ford Focus", "AB12 CDE", kind, Today.AddDays(daysLeft), Today);

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, 30)]
    [InlineData(8, 30)]
    [InlineData(7, 7)]
    [InlineData(0, 7)]
    [InlineData(-10, 7)]
    public void A_due_date_reaches_the_nearest_reminder_its_within(int daysLeft, int? expected) =>
        Assert.Equal(expected, Reminders.DaysBeforeFor(Today.AddDays(daysLeft), Today));

    [Theory]
    [InlineData(10, "in 10 days")]
    [InlineData(1, "tomorrow")]
    [InlineData(0, "today")]
    [InlineData(-1, "1 day overdue")]
    [InlineData(-3, "3 days overdue")]
    public void Says_how_soon_a_date_is(int daysLeft, string expected) =>
        Assert.Equal(expected, Reminders.When(Today.AddDays(daysLeft), Today));

    [Fact]
    public void Sends_each_reminder_once()
    {
        var mot = Due(30);
        var first = Reminders.ToSend([mot], new HashSet<ReminderKey>());
        Assert.Equal(30, Assert.Single(first).DaysBefore);

        var sent = new HashSet<ReminderKey> { ReminderKey.For(1, DueDateKind.Mot, mot.Date, null, 30) };
        Assert.Empty(Reminders.ToSend([mot], sent));
        Assert.Empty(Reminders.ToSend([mot with { Today = Today.AddDays(20) }], sent));   // 10 days left: still the 30-day reminder

        // A week before, the 7-day reminder.
        Assert.Equal(7, Assert.Single(Reminders.ToSend([mot with { Today = Today.AddDays(23) }], sent)).DaysBefore);
    }

    [Fact]
    public void A_date_first_seen_inside_a_week_gets_only_the_7_day_reminder() =>
        Assert.Equal(7, Assert.Single(Reminders.ToSend([Due(3)], new HashSet<ReminderKey>())).DaysBefore);

    [Fact]
    public void A_new_due_date_is_reminded_about_afresh()
    {
        var sent = new HashSet<ReminderKey> { ReminderKey.For(1, DueDateKind.Mot, Today.AddDays(30), null, 30) };
        Assert.Single(Reminders.ToSend([Due(29)], sent));
    }

    [Fact]
    public void Reminders_are_kept_apart_by_vehicle_and_kind()
    {
        var sent = new HashSet<ReminderKey> { ReminderKey.For(1, DueDateKind.Mot, Today.AddDays(20), null, 30) };
        var toSend = Reminders.ToSend([Due(20), Due(20, DueDateKind.Tax), Due(20, vehicleId: 2), Due(40, DueDateKind.Insurance)], sent);
        Assert.Equal([(1, DueDateKind.Tax), (2, DueDateKind.Mot)], toSend.Select(r => (r.Due.VehicleId, r.Due.Kind)).Order());
    }

    [Fact]
    public void A_service_due_by_mileage_is_reminded_about_once_however_its_estimate_moves()
    {
        var service = Due(25, DueDateKind.Service) with { DueMileage = 45000, MilesPerMonth = 1000 };
        var sent = new HashSet<ReminderKey> { ReminderKey.For(service, 30) };
        Assert.Empty(Reminders.ToSend([service with { Date = Today.AddDays(20) }], sent));

        // A different mileage (the next service) is a new reminder.
        Assert.Single(Reminders.ToSend([service with { DueMileage = 55000 }], sent));
    }

    [Fact]
    public void Describes_a_reminder()
    {
        Assert.Equal("MOT for Ford Focus (AB12 CDE) is due on 06/11/2026 (in 30 days).", Reminders.Describe(Due(30)));
        Assert.Equal("Tax for Ford Focus (AB12 CDE) was due on 05/10/2026 (2 days overdue).", Reminders.Describe(Due(-2, DueDateKind.Tax)));
        Assert.Equal("Service for Ford Focus (AB12 CDE) is due at 45,000 miles, estimated 06/11/2026 (in 30 days) from about 1,050 miles a month.",
            Reminders.Describe(Due(30, DueDateKind.Service) with { DueMileage = 45000, MilesPerMonth = 1050.4 }));
    }

    [Theory]
    [InlineData(-1, "text-bg-danger", true)]
    [InlineData(7, "text-bg-warning", true)]
    [InlineData(30, "text-bg-info", true)]
    [InlineData(31, "text-bg-secondary", false)]
    public void Due_dates_are_coloured_by_how_soon_they_are(int daysLeft, string badge, bool soon)
    {
        var due = Due(daysLeft);
        Assert.Equal((badge, soon), (due.BadgeClass, due.IsSoon));
    }

    [Theory]
    [InlineData(null, 40, true)]    // mileage only
    [InlineData(60, 40, true)]      // the mileage comes first
    [InlineData(20, 40, false)]     // the date comes first
    public void A_service_is_due_at_the_date_or_mileage_whichever_comes_first(int? dateDays, int mileageDays, bool byMileage)
    {
        // 30 miles a day, so 1,200 more miles takes 40 days.
        var trend = new MileageTrend(Today, 40000, 30);
        var vehicle = new VehicleView
        {
            Id = 1, Make = "Ford", Model = "Focus", RegistrationNumber = "AB12 CDE",
            ServiceDueDate = dateDays is { } d ? Today.AddDays(d) : null, ServiceDueMileage = 40000 + mileageDays * 30
        };

        var service = Assert.Single(vehicle.DueDates(Today, trend));
        Assert.Equal(DueDateKind.Service, service.Kind);
        if (byMileage)
        {
            Assert.Equal((Today.AddDays(40), (int?)41200, true), (service.Date, service.DueMileage, service.IsEstimate));
            Assert.Equal($"≈{Today.AddDays(40):dd/MM/yyyy}", service.DateText);
            Assert.Equal("at 41,200 miles, estimated from about 913 miles a month", service.Detail);
        }
        else
        {
            Assert.Equal((Today.AddDays(20), (int?)null), (service.Date, service.DueMileage));
            Assert.Null(service.Detail);
        }
    }

    [Fact]
    public void A_service_mileage_without_a_trend_has_no_date() =>
        Assert.Empty(new VehicleView { ServiceDueMileage = 45000 }.DueDates(Today));

    [Fact]
    public void A_vehicles_due_dates_are_the_ones_set_soonest_first()
    {
        var vehicle = new VehicleView
        {
            Id = 4, Make = "Ford", Model = "Focus", RegistrationNumber = "AB12 CDE",
            MotDueDate = Today.AddDays(50), InsuranceEndDate = Today.AddDays(5).ToDateTime(new TimeOnly(23, 59)), ServiceDueDate = Today.AddDays(50)
        };
        var dueDates = vehicle.DueDates(Today);
        Assert.Equal([DueDateKind.Insurance, DueDateKind.Mot, DueDateKind.Service], dueDates.Select(d => d.Kind));
        Assert.All(dueDates, d => Assert.Equal((4, "Ford Focus"), (d.VehicleId, d.VehicleName)));
    }
}
