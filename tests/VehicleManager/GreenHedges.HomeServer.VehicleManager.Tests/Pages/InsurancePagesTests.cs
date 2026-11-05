using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class InsurancePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = Reminders.UkToday();

    private const string Format = "yyyy-MM-ddTHH:mm";

    /// <summary>A new vehicle, and its owner's id (the test data's "Test Owner", shared by them all).</summary>
    private async Task<(int VehicleId, int OwnerId)> AddVehicleAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration());
        return (vehicle.Id, vehicle.OwnerId);
    }

    private static DateTime At(DateOnly date, int hour, int minute) => date.ToDateTime(new TimeOnly(hour, minute));

    private static Dictionary<string, string> Fields(int ownerId, int? vehicleId, DateTime start, DateTime end, string provider = "Acme Insurance",
        string number = "POL-1", string premium = "456.78")
    {
        var fields = new Dictionary<string, string>
        {
            ["Policy.OwnerId"] = ownerId.ToString(), ["Policy.StartDate"] = start.ToString(Format), ["Policy.EndDate"] = end.ToString(Format),
            ["Policy.Provider"] = provider, ["Policy.PolicyNumber"] = number, ["Policy.Premium"] = premium
        };
        if (vehicleId is { } id) fields["Policy.VehicleId"] = id.ToString();
        return fields;
    }

    /// <summary>Adds a policy through the form; returns the response (a redirect to the policy's page when it worked).</summary>
    private static async Task<PageResponse> AddPolicyAsync(PageClient client, Dictionary<string, string> fields)
    {
        var form = await client.GetAsync("/Insurance/Create");
        Assert.True(form.Status == HttpStatusCode.OK, $"{form.Status}: {form.Text[..Math.Min(form.Text.Length, 3000)]}");
        return await client.PostFormAsync("/Insurance/Create", form, fields);
    }

    private static int PolicyIdFrom(PageResponse added)
    {
        Assert.True(added.Status == HttpStatusCode.Redirect, added.Errors);
        return int.Parse(Regex.Match(added.Location!, @"^/Insurance/Details/(\d+)$").Groups[1].Value);
    }

    private static string Rows(PageResponse page) =>   // not the invisible sizing rows
        Regex.Match(page.Html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;

    [DatabaseFact]
    public async Task Adding_a_policy_for_an_owner_and_vehicle_sets_the_vehicles_insurance_renewal()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var start = At(Today.AddDays(-30), 14, 32);
        var end = At(Today.AddDays(20), 23, 59);

        var fields = Fields(ownerId, vehicleId, start, end, " Acme   Insurance ");
        fields["Policy.Cover"] = nameof(InsuranceCover.ThirdPartyFireAndTheft);
        fields["Policy.Excess"] = "350";
        fields["Policy.NamedDrivers"] = "Me, Partner";
        fields["Policy.Notes"] = "Protected no-claims bonus";
        var policyId = PolicyIdFrom(await AddPolicyAsync(client, fields));

        await using (var db = Db())
        {
            var policy = await db.InsurancePolicies.Include(p => p.Vehicles).SingleAsync();
            Assert.Equal((ownerId, start, end, "Acme Insurance", "POL-1", InsuranceCover.ThirdPartyFireAndTheft, 456.78m, (decimal?)350m, "Me, Partner"),
                (policy.OwnerId, policy.StartDate, policy.EndDate, policy.Provider, policy.PolicyNumber, policy.Cover, policy.Premium, policy.Excess, policy.NamedDrivers));
            var covered = Assert.Single(policy.Vehicles);
            Assert.Equal((vehicleId, start, end), (covered.VehicleId, covered.From, covered.To));
        }

        var policyPage = await client.GetAsync($"/Insurance/Details/{policyId}");
        Assert.Contains($"Added the Acme Insurance policy, {start:dd/MM/yyyy HH:mm} to {end:dd/MM/yyyy HH:mm}.", policyPage.Text);
        Assert.Contains("Test Owner's policy", policyPage.Text);
        Assert.Contains("Test Car", policyPage.Text);
        Assert.Contains("Protected no-claims bonus", policyPage.Text);

        var vehicle = await client.GetAsync($"/Vehicles/Details/{vehicleId}?tab=insurance");
        Assert.Contains($"Insurance {Today.AddDays(20):dd/MM/yyyy} · in 20 days", vehicle.Text);
        Assert.Contains("Insurance <span class=\"opacity-75\">(1)</span>", vehicle.Html);
        Assert.Matches(@"class=""tab-pane show active"" id=""tab-insurance""", vehicle.Html);
        Assert.Contains($"Its latest policy ends {end:dd/MM/yyyy HH:mm}", vehicle.Text);
        Assert.Contains($"href=\"/Insurance/Details/{policyId}\"", vehicle.Html);
        Assert.Contains("Third party, fire and theft", vehicle.Text);
        Assert.Contains(">Current</span>", Rows(vehicle));
        Assert.DoesNotContain("data-premium-chart", vehicle.Html);   // one point isn't a chart

        var list = await client.GetAsync("/Insurance");
        Assert.Contains("Policies (1)", list.Text);
        Assert.Contains("Test Car (", Rows(list));
        Assert.Contains("£456.78", list.Text);
    }

    [DatabaseFact]
    public async Task The_navbar_links_to_insurance_and_the_vehicle_form_has_no_insurance_date()
    {
        var (vehicleId, _) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var edit = await client.GetAsync($"/Vehicles/Edit/{vehicleId}");
        Assert.Contains("<a class=\"nav-link\" href=\"/Insurance\">Insurance</a>", edit.Html);
        Assert.DoesNotContain("InsuranceDueDate", edit.Html);
        Assert.Contains("Insurance renewal comes from the vehicle's insurance policies", edit.Text);
    }

    [DatabaseFact]
    public async Task Switching_vehicle_mid_term_moves_the_renewal_to_the_new_car_and_records_the_adjustment()
    {
        var (oldCar, ownerId) = await AddVehicleAsync();
        var (newCar, _) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var start = At(Today.AddDays(-100), 0, 0);
        var end = At(Today.AddDays(200), 23, 59);
        var policyId = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, oldCar, start, end, premium: "500")));
        int oldCover;
        await using (var db = Db()) oldCover = (await db.InsurancePolicyVehicles.SingleAsync()).Id;

        var switchUrl = $"/Insurance/{policyId}/Vehicles/Create?replace={oldCover}";
        Assert.Contains($"href=\"{switchUrl}\"", (await client.GetAsync($"/Insurance/Details/{policyId}")).Html);
        var form = await client.GetAsync(switchUrl);
        Assert.Contains("Switching from <strong>Test Car (", form.Html);
        var switchAt = At(Today.AddDays(-10), 9, 15);
        var switched = await client.PostFormAsync(switchUrl, form, new()
        {
            ["Period.VehicleId"] = newCar.ToString(), ["Period.From"] = switchAt.ToString(Format), ["Period.Adjustment"] = "35.50"
        });
        Assert.Equal((HttpStatusCode.Redirect, $"/Insurance/Details/{policyId}"), (switched.Status, switched.Location));

        await using (var db = Db())
        {
            var cover = await db.InsurancePolicyVehicles.OrderBy(v => v.From).ToListAsync();
            Assert.Equal([(oldCar, start, switchAt, (decimal?)null), (newCar, switchAt, end, 35.50m)],
                cover.Select(v => (v.VehicleId, v.From, v.To, v.Adjustment)));
        }

        // The new car renews it; the old one, switched off, has nothing to renew.
        Assert.Contains($"Insurance {Today.AddDays(200):dd/MM/yyyy} · in 200 days", (await client.GetAsync($"/Vehicles/Details/{newCar}")).Text);
        var old = await client.GetAsync($"/Vehicles/Details/{oldCar}?tab=insurance");
        Assert.DoesNotContain("Insurance " + Today.AddDays(200).ToString("dd/MM/yyyy"), old.Text);
        Assert.Contains(">Switched off</span>", Rows(old));

        var page = await client.GetAsync($"/Insurance/Details/{policyId}");
        Assert.Contains("With adjustments", page.Text);
        Assert.Contains("£535.50", page.Text);
        Assert.Contains("£35.50", page.Text);

        // Both cars' shares are charted, a line each.
        var chart = WebUtility.HtmlDecode(Regex.Match((await client.GetAsync("/Insurance")).Html, @"data-premium-chart=""([^""]*)""").Groups[1].Value);
        Assert.Equal(2, Regex.Matches(chart, "\"vehicle\":").Count);
    }

    [DatabaseFact]
    public async Task A_switch_must_be_to_another_vehicle_while_the_replaced_one_is_covered()
    {
        var (oldCar, ownerId) = await AddVehicleAsync();
        var (newCar, _) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var start = At(Today, 0, 0);
        var policyId = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, oldCar, start, At(Today.AddYears(1), 23, 59))));
        int cover;
        await using (var db = Db()) cover = (await db.InsurancePolicyVehicles.SingleAsync()).Id;

        var url = $"/Insurance/{policyId}/Vehicles/Create?replace={cover}";
        var atTheStart = await client.PostFormAsync(url, await client.GetAsync(url), new()
        {
            ["Period.VehicleId"] = newCar.ToString(), ["Period.From"] = start.ToString(Format)
        });
        Assert.Equal(HttpStatusCode.OK, atTheStart.Status);
        Assert.Contains("The switch must be while the vehicle it replaces is covered", atTheStart.Errors);
        var same = await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Period.VehicleId"] = oldCar.ToString() });
        Assert.Contains("Choose the vehicle it's switching to.", same.Errors);

        await using var check = Db();
        Assert.Single(await check.InsurancePolicyVehicles.ToListAsync());
    }

    [DatabaseFact]
    public async Task A_multi_car_policy_renews_each_car_and_renewing_carries_them_over()
    {
        var (car1, ownerId) = await AddVehicleAsync();
        var (car2, _) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var start = At(Today.AddDays(-335), 0, 0);
        var end = At(Today.AddDays(30), 23, 59);
        var policyId = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, car1, start, end, premium: "600")));
        var add = await client.GetAsync($"/Insurance/{policyId}/Vehicles/Create");
        var added = await client.PostFormAsync($"/Insurance/{policyId}/Vehicles/Create", add, new()
        {
            ["Period.VehicleId"] = car2.ToString(), ["Period.From"] = start.ToString(Format), ["Period.To"] = end.ToString(Format)
        });
        Assert.True(added.Status == HttpStatusCode.Redirect, added.Errors);

        foreach (var car in new[] { car1, car2 })
            Assert.Contains($"Insurance {Today.AddDays(30):dd/MM/yyyy} · in 30 days", (await client.GetAsync($"/Vehicles/Details/{car}")).Text);

        // Renewing starts where it ends, with both cars and the same insurer.
        var renewUrl = $"/Insurance/Create?renew={policyId}";
        var renew = await client.GetAsync(renewUrl);
        Assert.Contains($"value=\"{At(Today.AddDays(31), 0, 0).ToString(Format)}\"", renew.Html);
        Assert.Contains($"value=\"{At(Today.AddDays(31).AddYears(1), 0, 0).AddMinutes(-1).ToString(Format)}\"", renew.Html);
        Assert.Contains("value=\"Acme Insurance\"", renew.Html);
        var renewed = PolicyIdFrom(await client.PostFormAsync(renewUrl, renew, new() { ["Policy.PolicyNumber"] = "POL-2", ["Policy.Premium"] = "650" }));

        await using var db = Db();
        Assert.Equal(new[] { car1, car2 }.Order(),
            (await db.InsurancePolicyVehicles.Where(v => v.InsurancePolicyId == renewed).Select(v => v.VehicleId).ToListAsync()).Order());
    }

    [DatabaseFact]
    public async Task From_a_vehicle_the_form_starts_a_renewal_of_its_latest_policy()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        await using (var db = Db())
        {
            var policy = TestData.Policy(await db.Vehicles.SingleAsync(v => v.Id == vehicleId), new DateOnly(2025, 11, 5), new DateOnly(2026, 11, 4), "Acme Insurance");
            policy.Cover = InsuranceCover.ThirdPartyOnly;
            policy.NamedDrivers = "Me";
            db.InsurancePolicies.Add(policy);
            await db.SaveChangesAsync();
        }

        var form = await app.ClientFor(UserId).GetAsync($"/Insurance/Create?vehicleId={vehicleId}");
        Assert.Contains("value=\"2026-11-05T00:00\"", form.Html);
        Assert.Contains("value=\"2027-11-04T23:59\"", form.Html);
        Assert.Contains("value=\"Acme Insurance\"", form.Html);
        Assert.Matches($@"<option selected=""selected"" value=""{vehicleId}"">", form.Html);
        Assert.Matches($@"<option selected=""selected"" value=""{ownerId}"">", form.Html);
        Assert.Matches(@"<option selected=""selected"" value=""ThirdPartyOnly"">", form.Html);
        Assert.Matches(@"id=""Policy_PolicyNumber""[^>]*value=""""", form.Html);
    }

    [DatabaseFact]
    public async Task A_policy_ending_at_midnight_is_due_the_day_before()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today.AddDays(-300), 0, 0), At(Today.AddDays(11), 0, 0))));
        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", (await client.GetAsync($"/Vehicles/Details/{vehicleId}")).Text);
    }

    [DatabaseFact]
    public async Task The_chart_shows_actual_costs_first_with_a_per_year_option()
    {
        var (vehicleId, _) = await AddVehicleAsync();
        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync(v => v.Id == vehicleId);
            db.InsurancePolicies.Add(TestData.Policy(vehicle, new DateOnly(2024, 11, 5), new DateOnly(2025, 11, 4), premium: 400m));
            // Half a year, so per year it's about twice what it cost.
            db.InsurancePolicies.Add(TestData.Policy(vehicle, new DateOnly(2025, 11, 5), new DateOnly(2026, 5, 4), premium: 250m));
            await db.SaveChangesAsync();
        }

        var page = await app.ClientFor(UserId).GetAsync($"/Vehicles/Details/{vehicleId}?tab=insurance");
        var json = WebUtility.HtmlDecode(Regex.Match(page.Html, @"data-premium-chart=""([^""]*)""").Groups[1].Value);
        Assert.Contains("\"points\":[{\"start\":\"2024-11-05T00:00\",\"end\":\"2025-11-04T23:59\",\"provider\":\"Test Insurer\",\"cost\":400", json);
        Assert.Matches(@"""start"":""2025-11-05T00:00"",""end"":""2026-05-04T23:59"",""provider"":""Test Insurer"",""cost"":250(\.0+)?,""yearly"":504\.\d+", json);
        Assert.Matches(@"class=""nav-link active"" data-premium-view=""actual"" aria-pressed=""true"">Actual<", page.Html);
        Assert.Contains("data-premium-view=\"yearly\"", page.Html);
        Assert.Matches(@"src=""/js/premium-chart\.[^""]*js""", page.Html);   // fingerprinted
        Assert.Contains("chart.umd.js", page.Html);
    }

    [DatabaseFact]
    public async Task A_policy_needs_its_owner_vehicle_dates_insurer_number_and_premium_and_must_end_after_it_starts()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);

        var missing = await AddPolicyAsync(client, new()
        {
            ["Policy.OwnerId"] = "", ["Policy.VehicleId"] = "", ["Policy.StartDate"] = "", ["Policy.EndDate"] = "",
            ["Policy.Provider"] = "", ["Policy.PolicyNumber"] = "", ["Policy.Premium"] = ""
        });
        Assert.Equal(HttpStatusCode.OK, missing.Status);
        foreach (var field in new[] { "Owner (policyholder)", "Starts", "Ends", "Insurer", "Policy number", "Premium (£)" })
            Assert.Contains($"The {field} field is required.", missing.Errors);
        Assert.Contains("Choose the vehicle it covers.", missing.Errors);

        var backwards = await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today, 12, 0), At(Today, 11, 59)));
        Assert.Contains("The policy must end after it starts.", backwards.Errors);

        await using var check = Db();
        Assert.Empty(await check.InsurancePolicies.ToListAsync());
    }

    [DatabaseFact]
    public async Task Editing_a_policys_dates_moves_its_vehicles_cover_and_a_conflicting_edit_isnt_saved()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var policyId = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today.AddDays(-30), 0, 0), At(Today.AddDays(20), 23, 59))));
        var url = $"/Insurance/Edit/{policyId}";

        var stale = await client.GetAsync(url);
        var saved = await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Policy.EndDate"] = At(Today.AddDays(25), 23, 59).ToString(Format) });
        Assert.Equal((HttpStatusCode.Redirect, $"/Insurance/Details/{policyId}"), (saved.Status, saved.Location));
        Assert.Contains($"Insurance {Today.AddDays(25):dd/MM/yyyy} · in 25 days", (await client.GetAsync($"/Vehicles/Details/{vehicleId}")).Text);

        var conflict = await client.PostFormAsync(url, stale, new() { ["Policy.Provider"] = "Second" });
        Assert.Equal(HttpStatusCode.OK, conflict.Status);
        Assert.Contains("Someone changed this policy after you opened it", conflict.Text);
    }

    [DatabaseFact]
    public async Task Deleting_the_latest_policy_takes_the_renewal_from_the_one_before()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today.AddDays(-300), 0, 0), At(Today.AddDays(10), 23, 59), number: "OLD")));
        var latest = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today.AddDays(11), 0, 0), At(Today.AddDays(375), 23, 59), number: "NEW")));

        var url = $"/Insurance/Delete/{latest}";
        var confirm = await client.GetAsync(url);
        var text = Regex.Replace(Regex.Replace(confirm.Text, "<[^>]+>", ""), @"\s+", " ");
        Assert.Contains($"Delete Test Owner's Acme Insurance policy NEW, {At(Today.AddDays(11), 0, 0):dd/MM/yyyy HH:mm} to {At(Today.AddDays(375), 23, 59):dd/MM/yyyy HH:mm}, £456.78?", text);
        var deleted = await client.PostFormAsync(url, confirm);
        Assert.Equal((HttpStatusCode.Redirect, "/Insurance"), (deleted.Status, deleted.Location));

        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", (await client.GetAsync($"/Vehicles/Details/{vehicleId}")).Text);
    }

    [DatabaseFact]
    public async Task An_owner_with_policies_cant_be_deleted_and_the_list_filters_by_owner()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        int otherOwner;
        await using (var db = Db())
        {
            db.Owners.Add(new Owner { Name = "Partner" });
            await db.SaveChangesAsync();
            otherOwner = (await db.Owners.SingleAsync(o => o.Name == "Partner")).Id;
        }
        var client = app.ClientFor(UserId);
        PolicyIdFrom(await AddPolicyAsync(client, Fields(otherOwner, vehicleId, At(Today, 0, 0), At(Today.AddYears(1), 23, 59), "Partner Insurer")));

        var delete = await client.GetAsync($"/Owners/Delete/{otherOwner}");
        Assert.Contains("can't be deleted while they hold insurance policies (1)", delete.Text);
        await client.PostFormAsync($"/Owners/Delete/{otherOwner}", delete);
        await using (var db = Db()) Assert.True(await db.Owners.AnyAsync(o => o.Id == otherOwner));

        Assert.Contains("Partner Insurer", Rows(await client.GetAsync($"/Insurance?ownerId={otherOwner}")));
        Assert.DoesNotContain("Partner Insurer", Rows(await client.GetAsync($"/Insurance?ownerId={ownerId}")));
    }

    [DatabaseFact]
    public async Task A_policy_and_its_vehicles_arent_found_by_another_user_or_under_another_policy()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var policyId = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today, 0, 0), At(Today.AddYears(1), 23, 59))));
        var otherPolicy = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today, 0, 0), At(Today.AddYears(1), 23, 59), number: "OTHER")));
        int cover;
        await using (var db = Db()) cover = (await db.InsurancePolicyVehicles.SingleAsync(v => v.InsurancePolicyId == policyId)).Id;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Insurance/{otherPolicy}/Vehicles/Edit/{cover}")).Status);
        await client.PostFormAsync($"/Insurance/{otherPolicy}/Vehicles/Delete/{cover}", await client.GetAsync($"/Insurance/{policyId}/Vehicles/Delete/{cover}"));

        var other = app.ClientFor(NewUser());
        foreach (var url in new[] { $"/Insurance/Details/{policyId}", $"/Insurance/Edit/{policyId}", $"/Insurance/Delete/{policyId}",
                     $"/Insurance/Create?renew={policyId}", $"/Insurance/Create?vehicleId={vehicleId}", $"/Insurance/{policyId}/Vehicles/Create",
                     $"/Insurance/{policyId}/Vehicles/Edit/{cover}" })
            Assert.True((await other.GetAsync(url)).Status == HttpStatusCode.NotFound, url);
        Assert.DoesNotContain("Acme Insurance", (await other.GetAsync("/Insurance")).Text);

        await using var check = Db();
        Assert.Equal(2, await check.InsurancePolicyVehicles.CountAsync());
    }

    [DatabaseFact]
    public async Task Removing_a_vehicle_from_a_policy_or_deleting_the_vehicle_keeps_the_policy()
    {
        var (vehicleId, ownerId) = await AddVehicleAsync();
        var (otherId, _) = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var policyId = PolicyIdFrom(await AddPolicyAsync(client, Fields(ownerId, vehicleId, At(Today, 0, 0), At(Today.AddYears(1), 23, 59))));
        var add = await client.GetAsync($"/Insurance/{policyId}/Vehicles/Create");
        var added = await client.PostFormAsync($"/Insurance/{policyId}/Vehicles/Create", add, new() { ["Period.VehicleId"] = otherId.ToString() });
        Assert.True(added.Status == HttpStatusCode.Redirect, added.Errors);
        int cover;
        await using (var db = Db()) cover = (await db.InsurancePolicyVehicles.SingleAsync(v => v.VehicleId == otherId)).Id;

        var url = $"/Insurance/{policyId}/Vehicles/Delete/{cover}";
        var removed = await client.PostFormAsync(url, await client.GetAsync(url));
        Assert.Equal((HttpStatusCode.Redirect, $"/Insurance/Details/{policyId}"), (removed.Status, removed.Location));

        await using (var db = Db())
        {
            Assert.Equal([vehicleId], await db.InsurancePolicyVehicles.Select(v => v.VehicleId).ToListAsync());
            await db.Vehicles.Where(v => v.Id == vehicleId).ExecuteDeleteAsync();
        }
        await using var check = Db();
        Assert.Empty(await check.InsurancePolicyVehicles.ToListAsync());
        Assert.Single(await check.InsurancePolicies.ToListAsync());
    }
}
