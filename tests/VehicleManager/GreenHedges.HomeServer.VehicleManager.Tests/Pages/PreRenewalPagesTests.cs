using System.Net;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class PreRenewalPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    private static readonly DateOnly Today = Reminders.UkToday();

    /// <summary>
    /// An owner with a licence, a policy ending at 23:59 in 30 days (so renewing in 31), and claims 2, 3½ and 6 years
    /// before that renewal.
    /// </summary>
    private async Task<(int OwnerId, int PolicyId, DateOnly Renewal)> SetUpAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db, "RENEW 1");
        var renewal = Today.AddDays(31);
        var owner = await db.Owners.SingleAsync();
        (owner.DriverNumber, owner.LicenceIssueDate) = ("TESTA801015AB9CD", renewal.AddYears(-12).AddDays(1));   // 11 full years by then
        var policy = TestData.Policy(vehicle, Today.AddDays(31).AddYears(-1), Today.AddDays(30), "Acme Insurance");
        db.InsurancePolicies.Add(policy);
        foreach (var (date, fault, value) in new[]
                 {
                     (renewal.AddYears(-2), ClaimFault.Owner, 2400m),
                     (renewal.AddMonths(-42), ClaimFault.OtherParty, 1200m),
                     (renewal.AddYears(-6), ClaimFault.Both, 600m)
                 })
            db.Claims.Add(new Claim { OwnerId = owner.Id, VehicleId = vehicle.Id, Date = date, Fault = fault, TotalValue = value });
        await db.SaveChangesAsync();
        return (owner.Id, policy.Id, renewal);
    }

    [DatabaseFact]
    public async Task Shows_the_renewal_the_claims_to_declare_and_the_licence()
    {
        var (ownerId, policyId, renewal) = await SetUpAsync();
        var client = app.ClientFor(UserId);

        Assert.Contains($"href=\"/Insurance/PreRenewal?ownerId={ownerId}\"", (await client.GetAsync($"/Owners/Details/{ownerId}")).Html);
        Assert.Contains("href=\"/Insurance/PreRenewal\"", (await client.GetAsync("/Insurance")).Html);

        var page = await client.GetAsync($"/Insurance/PreRenewal?ownerId={ownerId}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains($"value=\"{renewal:yyyy-MM-dd}\"", page.Html);
        Assert.Contains($"href=\"/Insurance/Details/{policyId}\"", page.Html);
        Assert.Contains("data-pre-renewal=\"years-held\">11</span>", page.Html);
        Assert.Contains("TESTA801015AB9CD", page.Text);
        Assert.Contains("TESTA 801015 AB9CD", page.Text);
        foreach (var section in new[] { "TESTA", "801015", "AB", "9", "CD" })
            Assert.Contains($"data-copy=\"{section}\"", page.Html);

        Assert.Contains("Claims to declare (2)", page.Text);
        Assert.Contains("£2,400.00", page.Text);
        Assert.Contains("£1,200.00", page.Text);
        Assert.DoesNotContain("£600.00", page.Text);
        Assert.Contains("1 older claim needn't be declared.", page.Text);
        // 2 years before: every cut-off; 3½ years: just 4 and 5.
        var rows = page.Html[page.Html.IndexOf("Claims to declare", StringComparison.Ordinal)..];
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(rows, "title=\"Declare\"").Count);
    }

    [DatabaseFact]
    public async Task Another_renewal_date_can_be_chosen()
    {
        var (ownerId, _, renewal) = await SetUpAsync();
        var page = await app.ClientFor(UserId).GetAsync($"/Insurance/PreRenewal?ownerId={ownerId}&date={renewal.AddYears(2):yyyy-MM-dd}");
        Assert.Contains("data-pre-renewal=\"years-held\">13</span>", page.Html);
        Assert.Contains("Claims to declare (1)", page.Text);   // now exactly 4 years before (just the 5-year cut-off) and 5½ (none)
        Assert.Contains($"Back to {renewal:dd/MM/yyyy}", page.Text);
    }

    [DatabaseFact]
    public async Task Without_a_current_policy_or_licence_it_works_from_today()
    {
        await using (var db = Db()) await TestData.AddVehicleAsync(db, "RENEW 2");
        var page = await app.ClientFor(UserId).GetAsync("/Insurance/PreRenewal");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains($"value=\"{Today:yyyy-MM-dd}\"", page.Html);
        Assert.Contains("No current policy, so today.", page.Text);
        Assert.Contains("No first issue date.", page.Text);
        Assert.Contains("Claims to declare (0)", page.Text);
        Assert.Equal(HttpStatusCode.NotFound, (await app.ClientFor(UserId).GetAsync("/Insurance/PreRenewal?ownerId=999999")).Status);
    }

    [DatabaseFact]
    public async Task A_named_drivers_claims_and_licence_are_on_the_policyholders_pre_renewal()
    {
        int wifeId, meId;
        var renewal = Today.AddDays(31);
        await using (var db = Db())
        {
            var vehicle = await TestData.AddVehicleAsync(db, "RENEW 3");
            var me = await db.Owners.SingleAsync();
            (me.DriverNumber, me.LicenceIssueDate) = ("TESTA801015AB9CD", renewal.AddYears(-20));
            var wife = new Owner { Name = "Wife Owner", LicenceIssueDate = renewal.AddYears(-8) };
            db.Owners.Add(wife);
            await db.SaveChangesAsync();
            var policy = TestData.Policy(vehicle, Today.AddDays(31).AddYears(-1), Today.AddDays(30));
            policy.OwnerId = wife.Id;
            policy.Drivers.Add(new InsurancePolicyDriver { OwnerId = me.Id });
            db.InsurancePolicies.Add(policy);
            db.Claims.Add(new Claim { OwnerId = me.Id, VehicleId = vehicle.Id, Date = renewal.AddYears(-1), Fault = ClaimFault.Owner, TotalValue = 3100m });
            await db.SaveChangesAsync();
            (wifeId, meId) = (wife.Id, me.Id);
        }

        var page = await app.ClientFor(UserId).GetAsync($"/Insurance/PreRenewal?ownerId={wifeId}");
        var mine = page.Html[page.Html.IndexOf($"data-person=\"{meId}\"", StringComparison.Ordinal)..];
        var hers = page.Html[page.Html.IndexOf($"data-person=\"{wifeId}\"", StringComparison.Ordinal)..page.Html.IndexOf($"data-person=\"{meId}\"", StringComparison.Ordinal)];
        Assert.Contains("Policyholder", hers);
        Assert.Contains("data-pre-renewal=\"years-held\">8</span>", hers);
        Assert.Contains("Claims to declare (0)", hers);
        Assert.Contains("Named driver", mine);
        Assert.Contains("data-pre-renewal=\"years-held\">20</span>", mine);
        Assert.Contains("TESTA801015AB9CD", mine);
        Assert.Contains("Claims to declare (1)", mine);
        Assert.Contains("3,100.00", mine);
    }
}
