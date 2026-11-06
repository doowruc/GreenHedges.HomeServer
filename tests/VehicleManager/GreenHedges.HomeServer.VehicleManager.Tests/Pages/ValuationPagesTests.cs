using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class ValuationPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = Reminders.UkToday();

    /// <summary>
    /// A current policy covering "VALUE 1" to its end and "VALUE 2" only for its first month, plus "VALUE 3" (on the
    /// policy too, but disposed of). VALUE 1 has odometer readings: 30 miles a day, at 40,000 ten days ago.
    /// </summary>
    private async Task<(int OwnerId, int VehicleId, int SwitchedOffId, int DisposedId)> SetUpAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db, "VALUE 1");
        var switchedOff = await TestData.AddVehicleAsync(db, "VALUE 2");
        var disposed = await TestData.AddVehicleAsync(db, "VALUE 3");
        (disposed.DisposalMethod, disposed.DisposedDate) = (DisposalMethod.Sold, Today.AddDays(-1));
        var policy = TestData.Policy(vehicle, Today.AddMonths(-6), Today.AddMonths(6).AddDays(-1));
        policy.Vehicles.Add(new InsurancePolicyVehicle { VehicleId = switchedOff.Id, From = policy.StartDate, To = policy.StartDate.AddMonths(1) });
        policy.Vehicles.Add(new InsurancePolicyVehicle { VehicleId = disposed.Id, From = policy.StartDate, To = policy.EndDate });
        db.InsurancePolicies.Add(policy);
        foreach (var (date, odometer) in new[] { (Today.AddDays(-110), 36700), (Today.AddDays(-10), 39700) })
        {
            var fillUp = await TestData.AddFillUpAsync(db, vehicle.Id, date, ("Shell", "Hill Top"));
            fillUp.Odometer = odometer;
        }
        await db.SaveChangesAsync();
        return (vehicle.OwnerId, vehicle.Id, switchedOff.Id, disposed.Id);
    }

    private static Dictionary<string, string> Fields(int ownerId, string value, string type = nameof(ValuationType.Trade),
        string grade = nameof(ValuationGrade.Average), string source = nameof(ValuationSource.Parkers), DateOnly? date = null, string mileage = "40000") => new()
    {
        ["ownerId"] = ownerId.ToString(),
        ["date"] = "",   // the renewal date picker is in another form, which a browser wouldn't send
        ["Valuation.Date"] = (date ?? Today).ToString("yyyy-MM-dd"),
        ["Valuation.Mileage"] = mileage,
        ["Valuation.Value"] = value,
        ["Valuation.Type"] = type,
        ["Valuation.Grade"] = grade,
        ["Valuation.Source"] = source
    };

    [DatabaseFact]
    public async Task Pre_renewal_lists_the_vehicles_on_the_policy_to_value_with_their_mileage_and_the_sites()
    {
        var (ownerId, vehicleId, switchedOffId, disposedId) = await SetUpAsync();
        var page = await app.ClientFor(UserId).GetAsync($"/Insurance/PreRenewal?ownerId={ownerId}");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains($"data-vehicle=\"{vehicleId}\"", page.Html);
        Assert.DoesNotContain($"data-vehicle=\"{switchedOffId}\"", page.Html);   // off the policy before its end
        Assert.DoesNotContain($"data-vehicle=\"{disposedId}\"", page.Html);   // sold
        Assert.Contains("data-pre-renewal=\"mileage\">40,000</span>", page.Html);   // 39,700 + 10 days at 30 a day
        Assert.Contains("data-copy=\"VALUE 1\"", page.Html);
        Assert.Contains("data-copy=\"40000\"", page.Html);
        foreach (var site in ValuationSites.All) Assert.Contains($"href=\"{site.Url}\"", page.Html);
        Assert.Contains("None yet.", page.Text);
    }

    [DatabaseFact]
    public async Task Values_saved_on_pre_renewal_show_the_latest_the_one_before_and_the_change()
    {
        var (ownerId, vehicleId, _, _) = await SetUpAsync();
        var client = app.ClientFor(UserId);
        var url = $"/Insurance/PreRenewal?ownerId={ownerId}";
        var page = await client.GetAsync(url);

        var saved = await client.PostFormAsync($"/Insurance/PreRenewal?handler=Value&vehicleId={vehicleId}", page,
            Fields(ownerId, "9500", nameof(ValuationType.Private), "", nameof(ValuationSource.AutoTrader), Today.AddYears(-1), "30000"));
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        Assert.Equal($"/Insurance/PreRenewal?ownerId={ownerId}#vehicle-{vehicleId}", saved.Location);
        await client.PostFormAsync($"/Insurance/PreRenewal?handler=Value&vehicleId={vehicleId}", page, Fields(ownerId, "8250"));

        await using (var db = Db())
        {
            var latest = await db.VehicleValuations.OrderByDescending(v => v.Date).FirstAsync();
            Assert.Equal((vehicleId, Today, 40000, 8250m, ValuationType.Trade, ValuationGrade.Average, ValuationSource.Parkers),
                (latest.VehicleId, latest.Date, latest.Mileage, latest.Value, latest.Type, latest.Grade, latest.Source));
            Assert.Null((await db.VehicleValuations.OrderBy(v => v.Date).FirstAsync()).Grade);
        }

        page = await client.GetAsync(url);
        Assert.Contains("£8,250", page.Text);
        Assert.Contains("Trade (Average), Parkers", page.Text);
        Assert.Contains("(Private, Auto Trader)", page.Text);
        Assert.Contains("data-pre-renewal=\"change\">Down", page.Html);
        Assert.Contains("Down £1,250", page.Text);
        // The form starts like the latest: the same type, grade and source.
        Assert.Contains("value=\"Trade\" selected=\"selected\"", page.Html);
        Assert.Contains("value=\"Average\" selected=\"selected\"", page.Html);

        var vehiclePage = await client.GetAsync($"/Vehicles/Details/{vehicleId}?tab=insurance");
        Assert.Contains("Values (2)", vehiclePage.Text);
        Assert.Contains("Trade (Average)", vehiclePage.Text);
        Assert.Contains($"href=\"/Insurance/PreRenewal?ownerId={ownerId}\"", vehiclePage.Html);
    }

    [DatabaseFact]
    public async Task A_value_needs_its_type_and_source_and_cant_be_after_the_vehicle_went()
    {
        var (ownerId, vehicleId, _, disposedId) = await SetUpAsync();
        var client = app.ClientFor(UserId);
        var page = await client.GetAsync($"/Insurance/PreRenewal?ownerId={ownerId}");

        var missing = await client.PostFormAsync($"/Insurance/PreRenewal?handler=Value&vehicleId={vehicleId}", page,
            Fields(ownerId, "8000", type: "", source: ""));
        Assert.Equal(HttpStatusCode.OK, missing.Status);
        Assert.Contains("Choose the type of value.", missing.Text);
        Assert.Contains("Choose where the value came from.", missing.Text);
        Assert.Contains("value=\"8000\"", missing.Html);   // the posted value is kept

        var afterDisposal = await client.PostFormAsync($"/Insurance/PreRenewal?handler=Value&vehicleId={disposedId}", page, Fields(ownerId, "8000"));
        Assert.Contains($"The vehicle was disposed of on {Today.AddDays(-1):dd/MM/yyyy}.", afterDisposal.Text);

        await using var db = Db();
        Assert.False(await db.VehicleValuations.AnyAsync());
    }

    [DatabaseFact]
    public async Task Another_users_vehicle_cant_be_valued()
    {
        var (ownerId, vehicleId, _, _) = await SetUpAsync();
        var other = app.ClientFor(NewUser());
        var page = await other.GetAsync("/Insurance/PreRenewal");
        var posted = await other.PostFormAsync($"/Insurance/PreRenewal?handler=Value&vehicleId={vehicleId}", page, Fields(ownerId, "8000"));
        Assert.Equal(HttpStatusCode.NotFound, posted.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Vehicles/{vehicleId}/Valuations/Edit/1")).Status);
    }

    [DatabaseFact]
    public async Task A_value_can_be_edited_and_deleted_from_the_vehicle_page()
    {
        var (_, vehicleId, switchedOffId, _) = await SetUpAsync();
        int id;
        await using (var db = Db())
        {
            var valuation = new VehicleValuation
            {
                VehicleId = vehicleId, Date = Today.AddDays(-3), Mileage = 39800, Value = 8000m, Type = ValuationType.Retail, Source = ValuationSource.WhatCar
            };
            db.VehicleValuations.Add(valuation);
            await db.SaveChangesAsync();
            id = valuation.Id;
        }
        var client = app.ClientFor(UserId);
        var vehiclePage = await client.GetAsync($"/Vehicles/Details/{vehicleId}?tab=insurance");
        Assert.Contains($"href=\"/Vehicles/{vehicleId}/Valuations/Edit/{id}\"", vehiclePage.Html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Vehicles/{switchedOffId}/Valuations/Edit/{id}")).Status);   // not under that vehicle

        var url = $"/Vehicles/{vehicleId}/Valuations/Edit/{id}";
        var edit = await client.GetAsync(url);
        Assert.Contains("value=\"8000.00\"", edit.Html);
        var saved = await client.PostFormAsync(url, edit, new() { ["Valuation.Value"] = "8100", ["Valuation.Grade"] = nameof(ValuationGrade.Good) });
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        Assert.Equal($"/Vehicles/Details/{vehicleId}?tab=insurance#vehicle-values", saved.Location);
        await using (var db = Db())
        {
            var valuation = await db.VehicleValuations.SingleAsync();
            Assert.Equal((8100m, ValuationGrade.Good, ValuationType.Retail, ValuationSource.WhatCar), (valuation.Value, valuation.Grade!.Value, valuation.Type, valuation.Source));
        }

        var deleteUrl = $"/Vehicles/{vehicleId}/Valuations/Delete/{id}";
        var delete = await client.GetAsync(deleteUrl);
        Assert.Contains("retail (good) value", delete.Text);
        await client.PostFormAsync(deleteUrl, delete);
        await using (var db = Db()) Assert.False(await db.VehicleValuations.AnyAsync());
    }

    [DatabaseFact]
    public async Task Without_a_policy_the_owners_current_vehicles_are_listed()
    {
        int vehicleId;
        await using (var db = Db()) vehicleId = (await TestData.AddVehicleAsync(db, "VALUE 4")).Id;
        var page = await app.ClientFor(UserId).GetAsync("/Insurance/PreRenewal");
        Assert.Contains($"data-vehicle=\"{vehicleId}\"", page.Html);
        Assert.Contains("No odometer readings.", page.Text);
    }
}
