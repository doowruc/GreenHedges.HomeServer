using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class DisposalPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = Reminders.UkToday();

    /// <summary>Two vehicles, the first bought for £9,000 a while ago with its insurance due in 10 days.</summary>
    private async Task<(int Sold, int Kept)> AddVehiclesAsync()
    {
        await using var db = Db();
        var sold = await TestData.AddVehicleAsync(db, "GONE 1");
        sold.PurchasedDate = Today.AddYears(-3);
        sold.PurchasePrice = 9000m;
        db.InsurancePolicies.Add(TestData.Policy(sold, Today.AddDays(10).AddYears(-1), Today.AddDays(10)));
        var kept = await TestData.AddVehicleAsync(db, "KEPT 1");
        await db.SaveChangesAsync();
        return (sold.Id, kept.Id);
    }

    private static Task<PageResponse> DisposeAsync(PageClient client, int id, PageResponse form, string method, DateOnly date, string price = "") =>
        client.PostFormAsync($"/Vehicles/Dispose/{id}", form, new()
        {
            ["Disposal.Method"] = method, ["Disposal.Date"] = date.ToString("yyyy-MM-dd"), ["Disposal.Price"] = price
        });

    [DatabaseFact]
    public async Task A_vehicle_sold_drops_out_of_reminders_and_choices_but_keeps_its_history()
    {
        var (sold, kept) = await AddVehiclesAsync();
        await using (var db = Db()) await TestData.AddFillUpAsync(db, sold, Today.AddDays(-20), ("Shell", "Meadowbank"));
        var client = app.ClientFor(UserId);

        var details = await client.GetAsync($"/Vehicles/Details/{sold}");
        Assert.Contains($"href=\"/Vehicles/Dispose/{sold}\"", details.Html);
        Assert.Contains("Purchased", details.Text);
        Assert.Contains("for £9,000", details.Text);

        var form = await client.GetAsync($"/Vehicles/Dispose/{sold}");
        Assert.Contains($"value=\"{Today:yyyy-MM-dd}\"", form.Html);
        Assert.DoesNotContain("Undo disposal", form.Text);
        var saved = await DisposeAsync(client, sold, form, nameof(DisposalMethod.Sold), Today.AddDays(-1), "6500");
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);

        details = await client.GetAsync($"/Vehicles/Details/{sold}");
        Assert.Contains($"Sold {Today.AddDays(-1):dd/MM/yyyy}", details.Text);
        Assert.Contains("Sale price £6,500 · lost £2,500", details.Text);
        Assert.DoesNotContain("in 10 days", details.Text);   // no due dates
        Assert.DoesNotContain("No MOT, tax, insurance or service dates", details.Text);
        Assert.Contains("still insured after it went", details.Text);   // its policy runs on
        Assert.Contains("Meadowbank", details.Text);   // its history stays

        var vehicles = await client.GetAsync("/Vehicles");
        Assert.Contains("Previous vehicles (1)", vehicles.Text);
        var previous = vehicles.Html[vehicles.Html.IndexOf("previous-vehicles", StringComparison.Ordinal)..];
        Assert.Contains("GONE 1", previous);
        Assert.DoesNotContain("KEPT 1", previous);
        Assert.Contains($"Sold {Today.AddDays(-1):dd/MM/yyyy}", previous);

        var dashboard = await client.GetAsync("/");
        // No card and nothing coming up (its spend stays in the charts).
        Assert.DoesNotContain($"href=\"/Vehicles/Details/{sold}\"", dashboard.Html);
        Assert.Contains($"href=\"/Vehicles/Details/{kept}\"", dashboard.Html);
        Assert.DoesNotContain(Today.AddDays(10).ToString("dd/MM/yyyy"), dashboard.Text);

        // Not offered for a new fill-up (the form defaults to a current vehicle) or a new policy.
        var fillUp = await client.GetAsync("/FuelLog/Create");
        Assert.DoesNotContain($"value=\"{sold}\"", fillUp.Html);
        Assert.Contains($"<option selected=\"selected\" value=\"{kept}\">", fillUp.Html);
        var policy = await client.GetAsync("/Insurance/Create");
        Assert.DoesNotContain("GONE 1", policy.Html);

        // A fill-up after it went can't be added, even from its own page.
        var late = await client.GetAsync($"/FuelLog/Create?vehicleId={sold}");
        var rejected = await client.PostFormAsync("/FuelLog/Create", late, new()
        {
            ["Entry.Date"] = Today.ToString("yyyy-MM-dd"), ["Entry.Litres"] = "40", ["Entry.CostPerLitre"] = "1.5",
            ["Entry.Stops[0].Brand"] = "Shell", ["Entry.Stops[0].Station"] = "Meadowbank"
        });
        Assert.Contains($"The vehicle was sold on {Today.AddDays(-1):dd/MM/yyyy}.", rejected.Text);
    }

    [DatabaseFact]
    public async Task A_disposal_can_be_undone()
    {
        var (sold, _) = await AddVehiclesAsync();
        var client = app.ClientFor(UserId);
        var saved = await DisposeAsync(client, sold, await client.GetAsync($"/Vehicles/Dispose/{sold}"), nameof(DisposalMethod.WrittenOff), Today);
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);

        var form = await client.GetAsync($"/Vehicles/Dispose/{sold}");
        Assert.Matches(@"<option selected=""selected"" value=""WrittenOff"">", form.Html);
        Assert.Contains("Undo disposal", form.Text);
        var undone = await client.PostFormAsync($"/Vehicles/Dispose/{sold}?handler=Undo", form);
        Assert.Equal(HttpStatusCode.Redirect, undone.Status);

        await using var db = Db();
        var vehicle = await db.Vehicles.SingleAsync(v => v.Id == sold);
        Assert.Equal((null, null, null), (vehicle.DisposalMethod, vehicle.DisposedDate, vehicle.DisposalPrice));
        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", (await client.GetAsync($"/Vehicles/Details/{sold}")).Text);
    }

    [DatabaseFact]
    public async Task A_disposal_is_after_the_purchase_and_the_last_fill_up_and_needs_a_method()
    {
        var (sold, _) = await AddVehiclesAsync();
        await using (var db = Db()) await TestData.AddFillUpAsync(db, sold, Today.AddDays(-5), ("Shell", "Meadowbank"));
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync($"/Vehicles/Dispose/{sold}");

        Assert.Contains($"It can't go before it was bought ({Today.AddYears(-3):dd/MM/yyyy}).",
            (await DisposeAsync(client, sold, form, nameof(DisposalMethod.Scrapped), Today.AddYears(-4))).Text);
        Assert.Contains($"It was filled up after then (on {Today.AddDays(-5):dd/MM/yyyy}).",
            (await DisposeAsync(client, sold, form, nameof(DisposalMethod.Scrapped), Today.AddDays(-6))).Text);
        Assert.Contains("Choose how it went.", (await DisposeAsync(client, sold, form, "", Today)).Text);
    }

    [DatabaseFact]
    public async Task A_vehicle_going_later_is_still_current_until_then_and_a_renewal_leaves_it_off()
    {
        var (sold, kept) = await AddVehiclesAsync();
        int policyId;
        await using (var db = Db())
        {
            var policy = await db.InsurancePolicies.Include(p => p.Vehicles).SingleAsync();
            policy.Vehicles.Add(new InsurancePolicyVehicle { VehicleId = kept, From = policy.StartDate, To = policy.EndDate });
            await db.SaveChangesAsync();
            policyId = policy.Id;
        }
        var client = app.ClientFor(UserId);
        var saved = await DisposeAsync(client, sold, await client.GetAsync($"/Vehicles/Dispose/{sold}"), nameof(DisposalMethod.PartExchanged), Today.AddDays(5));
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);

        var details = await client.GetAsync($"/Vehicles/Details/{sold}");
        Assert.Contains($"Part exchanged {Today.AddDays(5):dd/MM/yyyy}", details.Text);
        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", details.Text);
        Assert.DoesNotContain("Previous vehicles", (await client.GetAsync("/Vehicles")).Text);

        // The renewal (from day 11) carries over only the vehicle still owned.
        var renew = await client.GetAsync($"/Insurance/Create?renew={policyId}");
        Assert.DoesNotContain("GONE 1", renew.Text);
        Assert.Contains("KEPT 1", renew.Text);
    }
}
