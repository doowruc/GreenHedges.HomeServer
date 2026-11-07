using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class DueDatePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = Reminders.UkToday();

    private async Task<int> AddOwnerAsync()
    {
        await using var db = Db();
        db.Owners.Add(new Owner { Name = "Me" });
        await db.SaveChangesAsync();
        return (await db.Owners.SingleAsync()).Id;
    }

    [DatabaseFact]
    public async Task Due_dates_are_typed_in_and_shown_on_the_vehicle_and_the_dashboard()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration())).Id;
        var client = app.ClientFor(UserId);

        var edit = await client.GetAsync($"/Vehicles/Edit/{id}");
        Assert.Contains("MOT and tax are checked with the DVLA and DVSA each morning.", edit.Text);
        Assert.DoesNotContain("Vehicle.InsuranceDueDate", edit.Html);
        await using (var db = Db())
        {
            // Insurance renewal comes from the policy ending last: here, at 23:59 in 10 days.
            db.InsurancePolicies.Add(TestData.Policy(await db.Vehicles.SingleAsync(), Today.AddDays(10).AddYears(-1), Today.AddDays(10)));
            await db.SaveChangesAsync();
        }
        var saved = await client.PostFormAsync($"/Vehicles/Edit/{id}", edit, new()
        {
            ["Vehicle.ServiceDueDate"] = Today.AddDays(-2).ToString("yyyy-MM-dd"),
            ["Vehicle.MotDueDate"] = Today.AddDays(100).ToString("yyyy-MM-dd")
        });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);

        var details = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", details.Text);
        Assert.Contains($"Service {Today.AddDays(-2):dd/MM/yyyy} · 2 days overdue", details.Text);
        Assert.Contains($"MOT {Today.AddDays(100):dd/MM/yyyy} · in 100 days", details.Text);

        // The dashboard's Coming up list has only what's due within 30 days (or overdue), soonest first.
        var dashboard = await client.GetAsync("/");
        var comingUp = WebUtility.HtmlDecode(Regex.Match(dashboard.Html, @"Coming up</div>\s*<ul[^>]*>(.*?)</ul>", RegexOptions.Singleline).Groups[1].Value);
        var service = comingUp.IndexOf($"{Today.AddDays(-2):dd/MM/yyyy} · 2 days overdue", StringComparison.Ordinal);
        var insurance = comingUp.IndexOf($"{Today.AddDays(10):dd/MM/yyyy} · in 10 days", StringComparison.Ordinal);
        Assert.True(service >= 0 && insurance > service, comingUp);
        Assert.DoesNotContain(Today.AddDays(100).ToString("dd/MM/yyyy"), comingUp);

        // The vehicle's card shows all its due dates.
        var card = dashboard.Text[(dashboard.Text.IndexOf(comingUp, StringComparison.Ordinal) + comingUp.Length)..];
        Assert.Contains($"Service {Today.AddDays(-2):dd/MM/yyyy} · 2 days overdue", card);
        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", card);
        Assert.Contains($"MOT {Today.AddDays(100):dd/MM/yyyy} · in 100 days", card);
    }

    [DatabaseFact]
    public async Task A_service_due_by_mileage_is_estimated_from_recent_fill_ups()
    {
        int id;
        await using (var db = Db())
        {
            id = (await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration())).Id;
            (await TestData.AddFillUpAsync(db, id, Today.AddDays(-60), ("Shell", "Meadowbank"))).Odometer = 40000;
            await db.SaveChangesAsync();
        }
        var client = app.ClientFor(UserId);

        var edit = await client.GetAsync($"/Vehicles/Edit/{id}");
        var saved = await client.PostFormAsync($"/Vehicles/Edit/{id}", edit, new() { ["Vehicle.ServiceDueMileage"] = "43000" });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);

        // One reading isn't enough to estimate from.
        Assert.Contains("Service due at 43,000 miles (not enough odometer readings to estimate when)", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);

        // 2,000 miles in 60 days, so the last 1,000 take 30 days.
        await using (var db = Db())
        {
            (await TestData.AddFillUpAsync(db, id, Today, ("Shell", "Meadowbank"))).Odometer = 42000;
            await db.SaveChangesAsync();
        }
        var details = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Contains($"Service ≈{Today.AddDays(30):dd/MM/yyyy} · in 30 days · 43,000 mi", details.Text);
        Assert.Contains("about 1,015 miles a month lately", details.Text);
        Assert.DoesNotContain("not enough odometer readings", details.Text);
        Assert.Contains($"≈{Today.AddDays(30):dd/MM/yyyy} · in 30 days · 43,000 mi", (await client.GetAsync("/")).Text);
    }

    [DatabaseFact]
    public async Task MOT_test_readings_count_towards_the_estimate()
    {
        int id;
        await using (var db = Db())
        {
            var vehicle = await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration());
            vehicle.ServiceDueMileage = 41000;
            vehicle.MotTests.Add(new MotTest { CompletedDate = Today.AddDays(-60).ToDateTime(new TimeOnly(12, 0)), Passed = true, Odometer = 38000 });
            await db.SaveChangesAsync();
            id = vehicle.Id;
            (await TestData.AddFillUpAsync(db, id, Today, ("Shell", "Meadowbank"))).Odometer = 40000;
            await db.SaveChangesAsync();
        }
        var client = app.ClientFor(UserId);

        // 2,000 miles in the 60 days since the MOT, so the last 1,000 take 30 days.
        var details = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Contains($"Service ≈{Today.AddDays(30):dd/MM/yyyy} · in 30 days · 41,000 mi", details.Text);
        Assert.DoesNotContain("not enough odometer readings", details.Text);
        Assert.Contains($"≈{Today.AddDays(30):dd/MM/yyyy} · in 30 days · 41,000 mi", (await client.GetAsync("/")).Text);
    }

    [DatabaseFact]
    public async Task A_service_due_mileage_must_be_a_mileage()
    {
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db)).Id;
        var client = app.ClientFor(UserId);
        var invalid = await client.PostFormAsync($"/Vehicles/Edit/{id}", await client.GetAsync($"/Vehicles/Edit/{id}"), new() { ["Vehicle.ServiceDueMileage"] = "0" });
        Assert.Contains("Service due mileage must be between 1 and 9999999.", invalid.Errors);
    }

    [DatabaseFact]
    public async Task Nothing_coming_up_shows_no_list()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db);
        Assert.DoesNotContain("Coming up", (await app.ClientFor(UserId).GetAsync("/")).Text);
    }

    [DatabaseFact]
    public async Task Adding_a_vehicle_looks_up_its_MOT_and_tax()
    {
        var ownerId = await AddOwnerAsync();
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle
        {
            TaxDueDate = new(2027, 3, 1), MotTestExpiryDates = [new(2026, 11, 2)]
        });
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync("/Vehicles/Create");
        var created = await client.PostFormAsync("/Vehicles/Create", form, new()
        {
            ["Vehicle.OwnerId"] = ownerId.ToString(), ["Vehicle.Make"] = "Ford", ["Vehicle.Model"] = "Focus",
            ["Vehicle.RegistrationNumber"] = reg.ToLowerInvariant()
        });
        Assert.True(created.Status == HttpStatusCode.Redirect, created.Errors);

        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync();
            Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2027, 3, 1)), (vehicle.MotDueDate, vehicle.TaxDueDate));
        }
        Assert.Contains("Checked with the DVLA and DVSA: MOT due 02/11/2026, tax due 01/03/2027.", (await client.GetAsync(created.Location!)).Text);
    }

    [DatabaseFact]
    public async Task Typed_in_MOT_or_tax_dates_arent_looked_up_when_adding()
    {
        var ownerId = await AddOwnerAsync();
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { TaxDueDate = new(2027, 3, 1) });
        var client = app.ClientFor(UserId);

        await client.PostFormAsync("/Vehicles/Create", await client.GetAsync("/Vehicles/Create"), new()
        {
            ["Vehicle.OwnerId"] = ownerId.ToString(), ["Vehicle.Make"] = "Ford", ["Vehicle.Model"] = "Focus",
            ["Vehicle.RegistrationNumber"] = reg, ["Vehicle.TaxDueDate"] = "2026-12-01"
        });
        await using var db = Db();
        Assert.Equal(new DateOnly(2026, 12, 1), (await db.Vehicles.SingleAsync()).TaxDueDate);
    }

    [DatabaseFact]
    public async Task Checking_MOT_and_tax_from_the_vehicle_page()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle
        {
            DvlaFails = true, MotTestExpiryDates = [new(2026, 11, 2)]
        });
        int id;
        await using (var db = Db()) id = (await TestData.AddVehicleAsync(db, reg)).Id;
        var client = app.ClientFor(UserId);

        var page = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Contains("Check MOT", page.Text);
        Assert.Contains("Check tax", page.Text);
        var checkedPage = await client.PostFormAsync($"/Vehicles/Details/{id}?handler=Lookup", page);
        Assert.Equal((HttpStatusCode.Redirect, $"/Vehicles/Details/{id}"), (checkedPage.Status, checkedPage.Location));

        page = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Contains($"Checked {reg} with the DVLA and DVSA: MOT due 02/11/2026.", page.Text);
        Assert.Contains("The DVLA returned an error (500: System error occurred).", page.Text);   // the warning
        Assert.Contains("MOT 02/11/2026", page.Text);
    }

    [DatabaseFact]
    public async Task Another_users_vehicle_cant_be_checked()
    {
        int id;
        await using (var db = TestDatabase.As(NewUser())) id = (await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration())).Id;
        await using (var db = Db()) await TestData.AddVehicleAsync(db);
        var client = app.ClientFor(UserId);

        var page = await client.GetAsync("/Vehicles");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync($"/Vehicles/Details/{id}?handler=Lookup", page)).Status);
    }

    [DatabaseFact]
    public async Task Saving_the_reminder_email()
    {
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync("/Settings");
        Assert.Contains("Reminders", form.Text);

        var saved = await client.PostFormAsync("/Settings", form, new() { ["Settings.ReminderEmail"] = " remind@tests.invalid " });
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);

        await using var db = Db();
        var settings = await db.UserSettings.SingleAsync();
        Assert.Equal("remind@tests.invalid", settings.ReminderEmail);
    }
}
