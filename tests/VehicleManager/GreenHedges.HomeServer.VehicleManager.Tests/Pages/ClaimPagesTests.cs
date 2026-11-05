using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class ClaimPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = Reminders.UkToday();

    /// <summary>A vehicle and its owner, with a policy covering it from a year ago to today.</summary>
    private async Task<(int VehicleId, int OwnerId, int PolicyId)> SetUpAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db, "CLAIM 1");
        var policy = TestData.Policy(vehicle, Today.AddYears(-1), Today, "Acme Insurance");
        db.InsurancePolicies.Add(policy);
        await db.SaveChangesAsync();
        return (vehicle.Id, vehicle.OwnerId, policy.Id);
    }

    private static Dictionary<string, string> Fields(int vehicleId, int? policyId, DateOnly date, string fault = nameof(ClaimFault.Owner), string value = "2400") => new()
    {
        ["Claim.VehicleId"] = vehicleId.ToString(), ["Claim.InsurancePolicyId"] = policyId?.ToString() ?? "", ["Claim.Date"] = date.ToString("yyyy-MM-dd"),
        ["Claim.Type"] = nameof(ClaimType.Accident), ["Claim.Fault"] = fault, ["Claim.TotalValue"] = value
    };

    private async Task<PageResponse> AddClaimAsync(PageClient client, int ownerId, Dictionary<string, string> fields)
    {
        var form = await client.GetAsync($"/Owners/{ownerId}/Claims/Create");
        Assert.Equal(HttpStatusCode.OK, form.Status);
        return await client.PostFormAsync($"/Owners/{ownerId}/Claims/Create", form, fields);
    }

    [DatabaseFact]
    public async Task A_claim_is_added_on_the_owners_page_and_shown_with_its_policy()
    {
        var (vehicleId, ownerId, policyId) = await SetUpAsync();
        var client = app.ClientFor(UserId);

        var owners = await client.GetAsync("/Owners");
        Assert.Contains($"href=\"/Owners/Details/{ownerId}\"", owners.Html);

        var form = await client.GetAsync($"/Owners/{ownerId}/Claims/Create");
        Assert.Contains("Acme Insurance TEST-1", form.Text);
        Assert.Contains("data-policy-cover=", form.Html);
        var added = await client.PostFormAsync($"/Owners/{ownerId}/Claims/Create", form, Fields(vehicleId, policyId, Today.AddMonths(-2), nameof(ClaimFault.Both), "2400.50"));
        Assert.True(added.Status == HttpStatusCode.Redirect, added.Errors);
        Assert.Equal($"/Owners/Details/{ownerId}", added.Location);
        // An old one, from before any recorded policy, with no policy.
        Assert.True((await AddClaimAsync(client, ownerId, Fields(vehicleId, null, Today.AddYears(-6), nameof(ClaimFault.OtherParty)))).Status == HttpStatusCode.Redirect);

        var owner = await client.GetAsync($"/Owners/Details/{ownerId}");
        Assert.Contains("Claims (2)", owner.Text);
        Assert.Contains("1 in the last 5 years, 1 at fault", owner.Text);
        Assert.Contains("Both (split)", owner.Text);
        Assert.Contains("£2,400.50", owner.Text);
        Assert.Contains($"href=\"/Insurance/Details/{policyId}\"", owner.Html);

        var policy = await client.GetAsync($"/Insurance/Details/{policyId}");
        Assert.Contains("Claims (1)", policy.Text);
        Assert.Contains("£2,400.50", policy.Text);
        Assert.Matches(@"<td class=""text-end"">2</td>", (await client.GetAsync("/Owners")).Html);   // the claims count

        await using var db = Db();
        var claim = await db.Claims.OrderByDescending(c => c.Date).FirstAsync();
        Assert.Equal((ownerId, vehicleId, (int?)policyId, ClaimType.Accident, ClaimFault.Both, 2400.50m),
            (claim.OwnerId, claim.VehicleId, claim.InsurancePolicyId, claim.Type, claim.Fault, claim.TotalValue));
    }

    [DatabaseFact]
    public async Task A_claims_policy_must_be_the_owners_and_cover_its_date()
    {
        var (vehicleId, ownerId, policyId) = await SetUpAsync();
        int otherPolicy;
        await using (var db = Db())
        {
            var other = new Owner { Name = "Other Owner" };
            db.Owners.Add(other);
            await db.SaveChangesAsync();
            var policy = TestData.Policy(await db.Vehicles.SingleAsync(), Today.AddYears(-1), Today);
            policy.OwnerId = other.Id;
            db.InsurancePolicies.Add(policy);
            await db.SaveChangesAsync();
            otherPolicy = policy.Id;
        }
        var client = app.ClientFor(UserId);

        Assert.Contains("so it doesn't cover the claim's date",
            (await AddClaimAsync(client, ownerId, Fields(vehicleId, policyId, Today.AddYears(-2)))).Text);
        Assert.Contains("Choose one of Test Owner's policies.", (await AddClaimAsync(client, ownerId, Fields(vehicleId, otherPolicy, Today.AddDays(-1)))).Text);
        Assert.Contains("Choose who was at fault.", (await AddClaimAsync(client, ownerId, Fields(vehicleId, null, Today.AddDays(-1), fault: ""))).Text);
        Assert.Contains("The date can't be in the future.", (await AddClaimAsync(client, ownerId, Fields(vehicleId, null, Today.AddDays(5)))).Text);
        await using var check = Db();
        Assert.Equal(0, await check.Claims.CountAsync());
    }

    [DatabaseFact]
    public async Task A_claim_can_be_edited_and_deleted()
    {
        var (vehicleId, ownerId, policyId) = await SetUpAsync();
        var client = app.ClientFor(UserId);
        await AddClaimAsync(client, ownerId, Fields(vehicleId, policyId, Today.AddDays(-10)));
        int claimId;
        await using (var db = Db()) claimId = (await db.Claims.SingleAsync()).Id;

        var url = $"/Owners/{ownerId}/Claims/Edit/{claimId}";
        var edit = await client.GetAsync(url);
        Assert.Matches($@"<option selected=""selected"" value=""{policyId}"">", edit.Html);
        var saved = await client.PostFormAsync(url, edit, new() { ["Claim.Fault"] = nameof(ClaimFault.OtherParty), ["Claim.TotalValue"] = "900" });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);
        Assert.Contains("0 at fault", (await client.GetAsync($"/Owners/Details/{ownerId}")).Text);

        // Another owner's URL doesn't reach it.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Owners/{ownerId + 1000}/Claims/Edit/{claimId}")).Status);

        var delete = await client.GetAsync($"/Owners/{ownerId}/Claims/Delete/{claimId}");
        Assert.Contains("£900.00", delete.Text);
        await client.PostFormAsync($"/Owners/{ownerId}/Claims/Delete/{claimId}", delete);
        await using var check = Db();
        Assert.Equal(0, await check.Claims.CountAsync());
    }

    [DatabaseFact]
    public async Task Claims_stop_their_owner_being_deleted_go_with_their_vehicle_and_outlive_their_policy()
    {
        var (vehicleId, ownerId, policyId) = await SetUpAsync();
        var client = app.ClientFor(UserId);
        await AddClaimAsync(client, ownerId, Fields(vehicleId, policyId, Today.AddDays(-10)));

        Assert.Contains("can't be deleted while they have insurance claims (1)", (await client.GetAsync($"/Owners/Delete/{ownerId}")).Text);

        var deletePolicy = await client.GetAsync($"/Insurance/Delete/{policyId}");
        Assert.Contains("Its 1 claim(s) are kept, without a policy.", deletePolicy.Text);
        await client.PostFormAsync($"/Insurance/Delete/{policyId}", deletePolicy);
        await using (var db = Db()) Assert.Null((await db.Claims.SingleAsync()).InsurancePolicyId);

        var deleteVehicle = await client.GetAsync($"/Vehicles/Delete/{vehicleId}");
        Assert.Contains("This will also delete its 1 insurance claim(s).", deleteVehicle.Text);
        await client.PostFormAsync($"/Vehicles/Delete/{vehicleId}", deleteVehicle);
        await using var check = Db();
        Assert.Equal(0, await check.Claims.CountAsync());
    }
}
