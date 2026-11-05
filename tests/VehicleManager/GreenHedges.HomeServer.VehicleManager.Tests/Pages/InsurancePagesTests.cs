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

    private async Task<int> AddVehicleAsync()
    {
        await using var db = Db();
        return (await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration())).Id;
    }

    private static Dictionary<string, string> Fields(DateTime start, DateTime end, string provider = "Acme Insurance", string number = "POL-1",
        string premium = "456.78") => new()
    {
        ["Policy.StartDate"] = start.ToString("yyyy-MM-ddTHH:mm"), ["Policy.EndDate"] = end.ToString("yyyy-MM-ddTHH:mm"),
        ["Policy.Provider"] = provider, ["Policy.PolicyNumber"] = number, ["Policy.Premium"] = premium
    };

    private async Task<PageResponse> AddPolicyAsync(PageClient client, int vehicleId, Dictionary<string, string> fields)
    {
        var form = await client.GetAsync($"/Vehicles/{vehicleId}/Insurance/Create");
        Assert.True(form.Status == HttpStatusCode.OK, $"{form.Status}: {form.Text[..Math.Min(form.Text.Length, 3000)]}");
        return await client.PostFormAsync($"/Vehicles/{vehicleId}/Insurance/Create", form, fields);
    }

    private static DateTime At(DateOnly date, int hour, int minute) => date.ToDateTime(new TimeOnly(hour, minute));

    [DatabaseFact]
    public async Task Adding_a_policy_lists_it_and_sets_the_insurance_renewal_from_its_end()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var start = At(Today.AddDays(-30), 14, 32);
        var end = At(Today.AddDays(20), 23, 59);

        var fields = Fields(start, end, " Acme   Insurance ");
        fields["Policy.Cover"] = nameof(InsuranceCover.ThirdPartyFireAndTheft);
        fields["Policy.Excess"] = "350";
        fields["Policy.NamedDrivers"] = "Me, Partner";
        fields["Policy.Notes"] = "Protected no-claims bonus";
        var added = await AddPolicyAsync(client, id, fields);
        Assert.Equal((HttpStatusCode.Redirect, $"/Vehicles/Details/{id}?tab=insurance"), (added.Status, added.Location));

        await using (var db = Db())
        {
            var policy = await db.InsurancePolicies.SingleAsync();
            Assert.Equal((start, end, "Acme Insurance", "POL-1", InsuranceCover.ThirdPartyFireAndTheft, 456.78m, (decimal?)350m, "Me, Partner"),
                (policy.StartDate, policy.EndDate, policy.Provider, policy.PolicyNumber, policy.Cover, policy.Premium, policy.Excess, policy.NamedDrivers));
        }

        var page = await client.GetAsync($"/Vehicles/Details/{id}?tab=insurance");
        Assert.Contains($"Added the Acme Insurance policy, {start:dd/MM/yyyy HH:mm} to {end:dd/MM/yyyy HH:mm}.", page.Text);
        Assert.Contains($"Insurance {Today.AddDays(20):dd/MM/yyyy} · in 20 days", page.Text);
        Assert.Contains("Insurance <span class=\"opacity-75\">(1)</span>", page.Html);
        Assert.Matches(@"class=""tab-pane show active"" id=""tab-insurance""", page.Html);
        Assert.Contains($"The latest ends {end:dd/MM/yyyy HH:mm}", page.Text);
        Assert.Contains("Third party, fire and theft", page.Text);
        Assert.Contains("£456.78", page.Text);
        Assert.Contains("£350.00", page.Text);
        Assert.Contains(">Current</span>", page.Html);
        Assert.Contains("Protected no-claims bonus", page.Text);
        Assert.Contains($"href=\"/Vehicles/{id}/Insurance/Edit/", page.Html);
        // One policy isn't a chart.
        Assert.DoesNotContain("data-premium-chart", page.Html);
    }

    [DatabaseFact]
    public async Task The_vehicle_form_has_no_insurance_date()
    {
        var id = await AddVehicleAsync();
        var edit = await app.ClientFor(UserId).GetAsync($"/Vehicles/Edit/{id}");
        Assert.DoesNotContain("InsuranceDueDate", edit.Html);
        Assert.Contains("Insurance renewal comes from the vehicle's insurance policies", edit.Text);
    }

    [DatabaseFact]
    public async Task The_form_starts_a_renewal_when_the_latest_policy_ends_with_its_details()
    {
        var id = await AddVehicleAsync();
        await using (var db = Db())
        {
            var policy = TestData.Policy(new DateOnly(2025, 11, 5), new DateOnly(2026, 11, 4), "Acme Insurance");
            policy.Cover = InsuranceCover.ThirdPartyOnly;
            policy.Excess = 250m;
            policy.NamedDrivers = "Me";
            (await db.Vehicles.SingleAsync(v => v.Id == id)).InsurancePolicies.Add(policy);
            await db.SaveChangesAsync();
        }

        var form = await app.ClientFor(UserId).GetAsync($"/Vehicles/{id}/Insurance/Create");
        Assert.Contains("value=\"2026-11-05T00:00\"", form.Html);
        Assert.Contains("value=\"2027-11-04T23:59\"", form.Html);
        Assert.Contains("value=\"Acme Insurance\"", form.Html);
        Assert.Contains("value=\"Me\"", form.Html);
        Assert.Matches(@"<option selected=""selected"" value=""ThirdPartyOnly"">", form.Html);
        Assert.Contains("data-providers=\"[&quot;Acme Insurance&quot;]\"", form.Html);
        // A new policy number and premium each time.
        Assert.Matches(@"id=""Policy_PolicyNumber""[^>]*value=""""", form.Html);
    }

    [DatabaseFact]
    public async Task Overlapping_policies_are_allowed_and_the_one_ending_last_sets_the_renewal()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        Assert.Equal(HttpStatusCode.Redirect, (await AddPolicyAsync(client, id, Fields(At(Today.AddDays(-100), 0, 0), At(Today.AddDays(200), 23, 59)))).Status);
        // A change of insurer part way through: starts before the first ends.
        var second = await AddPolicyAsync(client, id, Fields(At(Today.AddDays(-10), 9, 15), At(Today.AddDays(300), 9, 15), "Other Insurer", "OTH-2", "300"));
        Assert.True(second.Status == HttpStatusCode.Redirect, second.Errors);

        var page = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Contains($"Insurance {Today.AddDays(300):dd/MM/yyyy} · in 300 days", page.Text);
        var rows = Regex.Match(page.Html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;   // not the invisible sizing rows
        Assert.Equal(2, Regex.Matches(rows, ">Current</span>").Count);
    }

    [DatabaseFact]
    public async Task A_policy_ending_at_midnight_is_due_the_day_before()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddPolicyAsync(client, id, Fields(At(Today.AddDays(-300), 0, 0), At(Today.AddDays(11), 0, 0)));
        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);
    }

    [DatabaseFact]
    public async Task Two_or_more_policies_chart_the_premiums_actual_first_with_a_yearly_option()
    {
        var id = await AddVehicleAsync();
        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync(v => v.Id == id);
            vehicle.InsurancePolicies.Add(TestData.Policy(new DateOnly(2024, 11, 5), new DateOnly(2025, 11, 4), premium: 400m));
            // Half a year, so per year it's about twice what it cost.
            vehicle.InsurancePolicies.Add(TestData.Policy(new DateOnly(2025, 11, 5), new DateOnly(2026, 5, 4), premium: 250m));
            await db.SaveChangesAsync();
        }

        var page = await app.ClientFor(UserId).GetAsync($"/Vehicles/Details/{id}?tab=insurance");
        var json = WebUtility.HtmlDecode(Regex.Match(page.Html, @"data-premium-chart=""([^""]*)""").Groups[1].Value);
        Assert.StartsWith("[{\"start\":\"2024-11-05T00:00\",\"end\":\"2025-11-04T23:59\",\"provider\":\"Test Insurer\",\"premium\":400", json);
        Assert.Contains("{\"start\":\"2025-11-05T00:00\",\"end\":\"2026-05-04T23:59\"", json);
        Assert.Matches(@"class=""nav-link active"" data-premium-view=""actual"" aria-pressed=""true"">Actual<", page.Html);
        Assert.Contains("data-premium-view=\"yearly\"", page.Html);
        Assert.Matches(@"src=""/js/premium-chart\.[^""]*js""", page.Html);   // fingerprinted
        Assert.Contains("chart.umd.js", page.Html);
        Assert.Matches(@"£250\.00 <span class=""text-muted small"" title=""181 days' cover[^""]*"">\(£504\.49 a year\)", page.Text);
    }

    [DatabaseFact]
    public async Task A_policy_needs_its_dates_insurer_number_and_premium_and_must_end_after_it_starts()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);

        var missing = await AddPolicyAsync(client, id, new()
        {
            ["Policy.StartDate"] = "", ["Policy.EndDate"] = "", ["Policy.Provider"] = "", ["Policy.PolicyNumber"] = "", ["Policy.Premium"] = ""
        });
        Assert.Equal(HttpStatusCode.OK, missing.Status);
        foreach (var field in new[] { "Starts", "Ends", "Insurer", "Policy number", "Premium (£)" })
            Assert.Contains($"The {field} field is required.", missing.Errors);

        var backwards = await AddPolicyAsync(client, id, Fields(At(Today, 12, 0), At(Today, 11, 59)));
        Assert.Equal(HttpStatusCode.OK, backwards.Status);
        Assert.Contains("The policy must end after it starts.", backwards.Errors);

        await using var check = Db();
        Assert.Empty(await check.InsurancePolicies.ToListAsync());
    }

    [DatabaseFact]
    public async Task Editing_a_policy_moves_the_renewal_and_a_conflicting_edit_isnt_saved()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddPolicyAsync(client, id, Fields(At(Today.AddDays(-30), 0, 0), At(Today.AddDays(20), 23, 59)));
        int policyId;
        await using (var db = Db()) policyId = (await db.InsurancePolicies.SingleAsync()).Id;
        var url = $"/Vehicles/{id}/Insurance/Edit/{policyId}";

        var stale = await client.GetAsync(url);
        Assert.Contains($"value=\"{At(Today.AddDays(20), 23, 59):yyyy-MM-ddTHH:mm}\"", stale.Html);
        var saved = await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Policy.EndDate"] = At(Today.AddDays(25), 23, 59).ToString("yyyy-MM-ddTHH:mm") });
        Assert.Equal((HttpStatusCode.Redirect, $"/Vehicles/Details/{id}?tab=insurance"), (saved.Status, saved.Location));
        Assert.Contains($"Insurance {Today.AddDays(25):dd/MM/yyyy} · in 25 days", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);

        var conflict = await client.PostFormAsync(url, stale, new() { ["Policy.Provider"] = "Second" });
        Assert.Equal(HttpStatusCode.OK, conflict.Status);
        Assert.Contains("Someone changed this policy after you opened it", conflict.Text);
    }

    [DatabaseFact]
    public async Task Deleting_the_latest_policy_takes_the_renewal_from_the_one_before()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddPolicyAsync(client, id, Fields(At(Today.AddDays(-300), 0, 0), At(Today.AddDays(10), 23, 59), number: "OLD"));
        await AddPolicyAsync(client, id, Fields(At(Today.AddDays(11), 0, 0), At(Today.AddDays(375), 23, 59), number: "NEW"));
        int latest;
        await using (var db = Db()) latest = (await db.InsurancePolicies.SingleAsync(p => p.PolicyNumber == "NEW")).Id;

        var url = $"/Vehicles/{id}/Insurance/Delete/{latest}";
        var confirm = await client.GetAsync(url);
        var text = Regex.Replace(Regex.Replace(confirm.Text, "<[^>]+>", ""), @"\s+", " ");
        Assert.Contains($"Delete the Acme Insurance policy NEW for Test Car, {At(Today.AddDays(11), 0, 0):dd/MM/yyyy HH:mm} to {At(Today.AddDays(375), 23, 59):dd/MM/yyyy HH:mm}, £456.78?", text);
        var deleted = await client.PostFormAsync(url, confirm);
        Assert.Equal((HttpStatusCode.Redirect, $"/Vehicles/Details/{id}?tab=insurance"), (deleted.Status, deleted.Location));

        Assert.Contains($"Insurance {Today.AddDays(10):dd/MM/yyyy} · in 10 days", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);
    }

    [DatabaseFact]
    public async Task A_policy_is_only_found_under_its_own_vehicle_and_not_by_another_user()
    {
        var id = await AddVehicleAsync();
        var otherVehicle = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddPolicyAsync(client, id, Fields(At(Today, 0, 0), At(Today.AddYears(1), 23, 59)));
        int policyId;
        await using (var db = Db()) policyId = (await db.InsurancePolicies.SingleAsync()).Id;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Vehicles/{otherVehicle}/Insurance/Edit/{policyId}")).Status);
        await client.PostFormAsync($"/Vehicles/{otherVehicle}/Insurance/Delete/{policyId}", await client.GetAsync($"/Vehicles/{id}/Insurance/Delete/{policyId}"));

        var other = app.ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Vehicles/{id}/Insurance/Create")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Vehicles/{id}/Insurance/Edit/{policyId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Vehicles/{id}/Insurance/Delete/{policyId}")).Status);

        await using var check = Db();
        Assert.Single(await check.InsurancePolicies.ToListAsync());
    }

    [DatabaseFact]
    public async Task Deleting_a_vehicle_deletes_its_policies()
    {
        var id = await AddVehicleAsync();
        await using (var db = Db())
        {
            (await db.Vehicles.SingleAsync(v => v.Id == id)).InsurancePolicies.Add(TestData.Policy(Today, Today.AddYears(1)));
            await db.SaveChangesAsync();
            await db.Vehicles.Where(v => v.Id == id).ExecuteDeleteAsync();
        }
        await using var check = Db();
        Assert.Empty(await check.InsurancePolicies.ToListAsync());
    }
}
