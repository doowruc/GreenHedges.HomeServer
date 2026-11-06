using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class ServicePagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    /// <summary>A vehicle serviced every 12 months or 10,000 miles (or with the intervals given).</summary>
    private async Task<int> AddVehicleAsync(int? months = 12, int? miles = 10000)
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db, FakeVehicleApis.NewRegistration());
        vehicle.ServiceIntervalMonths = months;
        vehicle.ServiceIntervalMiles = miles;
        await db.SaveChangesAsync();
        return vehicle.Id;
    }

    private async Task<PageResponse> AddServiceAsync(PageClient client, int vehicleId, string date, int mileage, string garage = "Main Dealer", string location = "Leeds")
    {
        var form = await client.GetAsync($"/Vehicles/{vehicleId}/Services/Create");
        Assert.True(form.Status == HttpStatusCode.OK, $"{form.Status}: {form.Text[..Math.Min(form.Text.Length, 3000)]}");
        return await client.PostFormAsync($"/Vehicles/{vehicleId}/Services/Create", form, new()
        {
            ["Service.Date"] = date, ["Service.Mileage"] = mileage.ToString(), ["Service.Garage"] = garage, ["Service.GarageLocation"] = location
        });
    }

    private async Task<(DateOnly? Date, int? Mileage)> NextDueAsync(int vehicleId)
    {
        await using var db = Db();
        var v = await db.Vehicles.SingleAsync(v => v.Id == vehicleId);
        return (v.ServiceDueDate, v.ServiceDueMileage);
    }

    [DatabaseFact]
    public async Task Adding_a_service_works_out_the_next_one_and_lists_it_on_the_vehicle()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);

        var added = await AddServiceAsync(client, id, "2026-10-01", 45000, " Main  Dealer ");
        Assert.Equal((HttpStatusCode.Redirect, $"/Vehicles/Details/{id}?tab=services"), (added.Status, added.Location));
        Assert.Equal((new DateOnly(2027, 10, 1), (int?)55000), await NextDueAsync(id));

        var page = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Contains("Added the service on 01/10/2026. Next service due 01/10/2027 or at 55,000 miles.", page.Text);
        Assert.Matches(@"Service history\s*<span class=""period-stats"" data-period=""12m"">\(1\)</span><span class=""period-stats"" data-period=""all"">\(1\)</span>", page.Html);
        Assert.Contains("Every 12 months or 10,000 miles", page.Text);
        Assert.Matches(@"<td data-order=""2026-10-01"">01/10/2026</td>\s*<td class=""text-end"" data-order=""45000"">45,000</td>\s*<td>Main Dealer, Leeds</td>\s*<td>\s*</td>", page.Html);
        Assert.Contains($"href=\"/Vehicles/{id}/Services/Edit/", page.Html);
    }

    [DatabaseFact]
    public async Task The_form_starts_from_today_and_the_latest_odometer_and_suggests_garages_used_before()
    {
        var id = await AddVehicleAsync();
        await using (var db = Db())
        {
            (await TestData.AddFillUpAsync(db, id, new DateOnly(2026, 9, 1), ("Shell", "Meadowbank"))).Odometer = 44123;
            await db.SaveChangesAsync();
        }
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2025-10-01", 35000, "Main Dealer", "Leeds");

        var form = await client.GetAsync($"/Vehicles/{id}/Services/Create");
        Assert.Contains("value=\"44123\"", form.Html);
        Assert.Contains($"value=\"{Services.Reminders.UkToday():yyyy-MM-dd}\"", form.Html);
        Assert.Contains("data-garages=\"[&quot;Main Dealer&quot;]\"", form.Html);
        Assert.Contains("data-locations-by-garage=\"{&quot;main dealer&quot;:[&quot;Leeds&quot;]}\"", form.Html);
    }

    [DatabaseFact]
    public async Task A_service_under_a_previous_owner_is_marked_and_still_counts_towards_the_next_one()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync($"/Vehicles/{id}/Services/Create");
        Assert.Contains("Under a previous owner", form.Text);

        var added = await client.PostFormAsync($"/Vehicles/{id}/Services/Create", form, new()
        {
            ["Service.Date"] = "2024-05-01", ["Service.Mileage"] = "30000", ["Service.Garage"] = "Main Dealer",
            ["Service.UnderPreviousOwner"] = "true"
        });
        Assert.True(added.Status == HttpStatusCode.Redirect, added.Errors);
        Assert.Equal((new DateOnly(2025, 5, 1), (int?)40000), await NextDueAsync(id));

        var page = await client.GetAsync($"/Vehicles/Details/{id}");
        Assert.Matches(@"<td>Main Dealer</td>\s*<td>\s*<span class=""badge text-bg-secondary"">Previous owner</span>\s*</td>", page.Html);   // its own column; no location, so no comma

        int serviceId;
        await using (var db = Db()) serviceId = (await db.ServiceRecords.SingleAsync(s => s.UnderPreviousOwner)).Id;
        var edit = await client.GetAsync($"/Vehicles/{id}/Services/Edit/{serviceId}");
        Assert.Matches(@"<input class=""form-check-input"" type=""checkbox"" checked=""checked""[^>]*name=""Service.UnderPreviousOwner""", edit.Html);
        var confirm = await client.GetAsync($"/Vehicles/{id}/Services/Delete/{serviceId}");
        Assert.Contains("by Main Dealer, under a previous owner?", Regex.Replace(Regex.Replace(confirm.Text, "<[^>]+>", ""), @"\s+", " "));

        // Unticked, it's the user's own.
        var saved = await client.PostFormAsync($"/Vehicles/{id}/Services/Edit/{serviceId}", edit, new() { ["Service.UnderPreviousOwner"] = "false" });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);
        await using var check = Db();
        Assert.False((await check.ServiceRecords.SingleAsync()).UnderPreviousOwner);
        Assert.DoesNotContain("Previous owner</span>", (await client.GetAsync($"/Vehicles/Details/{id}")).Html);
    }

    [DatabaseFact]
    public async Task A_service_before_the_purchase_date_is_under_a_previous_owner()
    {
        var id = await AddVehicleAsync();
        await using (var db = Db())
        {
            (await db.Vehicles.SingleAsync()).PurchasedDate = new DateOnly(2025, 3, 1);
            await db.SaveChangesAsync();
        }
        var client = app.ClientFor(UserId);

        var form = await client.GetAsync($"/Vehicles/{id}/Services/Create");
        Assert.Contains("data-purchased=\"2025-03-01\"", form.Html);
        Assert.Contains("It's ticked for you for dates before you bought it (01/03/2025).", form.Text);

        // Without the box ticked (as without JavaScript): before the purchase date, after it, and on it.
        await AddServiceAsync(client, id, "2025-02-28", 30000);
        await AddServiceAsync(client, id, "2025-03-01", 31000);
        await AddServiceAsync(client, id, "2026-03-01", 41000);
        await using var check = Db();
        Assert.Equal([(30000, true), (31000, false), (41000, false)],
            (await check.ServiceRecords.OrderBy(s => s.Date).Select(s => new { s.Mileage, s.UnderPreviousOwner }).ToListAsync())
                .Select(s => (s.Mileage, s.UnderPreviousOwner)));
    }

    [DatabaseFact]
    public async Task Without_a_purchase_date_nothing_is_ticked()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync($"/Vehicles/{id}/Services/Create");
        Assert.Contains("data-purchased=\"\"", form.Html);
        Assert.DoesNotContain("It's ticked for you", form.Text);

        await AddServiceAsync(client, id, "2001-01-01", 1000);
        await using var check = Db();
        Assert.False((await check.ServiceRecords.SingleAsync()).UnderPreviousOwner);
    }

    [DatabaseFact]
    public async Task The_service_history_is_a_DataTable_with_the_fuel_logs_padding()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        Assert.DoesNotContain("datatables.net@", (await client.GetAsync($"/Vehicles/Details/{id}")).Html);   // no fill-ups or services yet

        await AddServiceAsync(client, id, "2026-10-01", 45000);
        await AddServiceAsync(client, id, "2025-10-01", 135000, "Local Garage", "");
        var page = (await client.GetAsync($"/Vehicles/Details/{id}")).Html;
        Assert.Contains("datatables.net@", page);
        Assert.Matches(@"<script src=""/js/data-table[^""]*\.js""", page);
        Assert.Matches(@"<div class=""card-body py-2""><div class=""data-table dt-pending"" data-noun=""services"">", page);
        Assert.Matches(@"<td data-order=""2026-10-01"">01/10/2026</td>\s*<td class=""text-end"" data-order=""45000"">45,000</td>", page);
        Assert.Contains("<th data-orderable=\"false\" data-searchable=\"false\"></th>", page);

        // The footer sizes the columns: each garage, and the longest mileage.
        var sizer = Regex.Match(page, @"<tfoot class=""column-sizer"".*?</tfoot>", RegexOptions.Singleline).Value;
        Assert.Contains("Main Dealer, Leeds", sizer);
        Assert.Contains("Local Garage", sizer);
        Assert.Contains("135,000", sizer);
    }

    [DatabaseFact]
    public async Task Services_later_than_the_interval_have_red_badges()
    {
        var id = await AddVehicleAsync();   // every 12 months or 10,000 miles
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2024-10-01", 30000);
        await AddServiceAsync(client, id, "2025-09-01", 41000);   // 1,000 miles over, a month early
        await AddServiceAsync(client, id, "2026-12-01", 56000);   // 3 months late and 5,000 miles over

        var page = (await client.GetAsync($"/Vehicles/Details/{id}")).Html;
        // The all-time table (the last 12 months' comes first).
        var body = Regex.Match(page, @"<div class=""period-stats"" data-period=""all"">.*?data-noun=""services"">.*?<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        // In the Notes column, with the previous owner badge: late by date, then by miles.
        Assert.Matches(@"(?s)01/12/2026</td>.*?56,000</td>\s*<td>[^<]*</td>\s*<td>\s*" +
                       @"<span class=""badge text-bg-danger"" title=""Due 01/09/2026, the service interval after the one before"">3 months late</span>\s*" +
                       @"<span class=""badge text-bg-danger"" title=""Due at 51,000 miles, the service interval after the one before"">5,000 miles over</span>\s*</td>", body);
        Assert.Matches(@"41,000</td>\s*<td>[^<]*</td>\s*<td>\s*<span class=""badge text-bg-danger""[^>]*>1,000 miles over</span>\s*</td>", body);
        Assert.Equal(3, Regex.Matches(body, "text-bg-danger").Count);   // none for the first, or the early date
    }

    [DatabaseFact]
    public async Task Fuel_and_services_are_on_separate_tabs_fuel_first()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);

        var page = (await client.GetAsync($"/Vehicles/Details/{id}")).Html;
        Assert.Matches(@"<ul class=""nav nav-tabs mb-3"" role=""tablist""", page);
        Assert.Matches($@"<a class=""nav-link active"" role=""tab"" data-bs-toggle=""tab"" data-bs-target=""#tab-fuel""[^>]*href=""/Vehicles/Details/{id}\?tab=fuel""", page);
        Assert.Matches($@"<a class=""nav-link\s*"" role=""tab"" data-bs-toggle=""tab"" data-bs-target=""#tab-services""[^>]*href=""/Vehicles/Details/{id}\?tab=services""", page);
        Assert.Contains("Services <span class=\"opacity-75\">(1)</span>", page);
        Assert.Contains("<div class=\"tab-pane show active\" id=\"tab-fuel\" role=\"tabpanel\">", page);
        Assert.Matches(@"<div class=""tab-pane\s*"" id=""tab-services"" role=""tabpanel"">", page);

        // The service history is in its tab, after the fuel tab's content.
        var services = page.IndexOf("id=\"tab-services\"", StringComparison.Ordinal);
        Assert.True(page.IndexOf("No fill-ups recorded yet.", StringComparison.Ordinal) < services);
        Assert.True(page.IndexOf("Service history", StringComparison.Ordinal) > services);
    }

    [DatabaseFact]
    public async Task The_services_tab_opens_from_the_address_and_after_saving_a_service()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);

        var added = await AddServiceAsync(client, id, "2026-10-01", 45000);
        Assert.Equal($"/Vehicles/Details/{id}?tab=services", added.Location);
        var page = (await client.GetAsync(added.Location!)).Html;
        Assert.Matches(@"<div class=""tab-pane\s*"" id=""tab-fuel"" role=""tabpanel"">", page);
        Assert.Contains("<div class=\"tab-pane show active\" id=\"tab-services\" role=\"tabpanel\">", page);
        Assert.Matches(@"<a class=""nav-link active"" role=""tab"" data-bs-toggle=""tab"" data-bs-target=""#tab-services""", page);

        // The service pages' Cancel links go back to it too.
        Assert.Matches($@"<a [^>]*href=""/Vehicles/Details/{id}\?tab=services""[^>]*>Cancel</a>", (await client.GetAsync($"/Vehicles/{id}/Services/Create")).Html);
        // Anything else opens the fuel tab.
        Assert.Contains("<div class=\"tab-pane show active\" id=\"tab-fuel\" role=\"tabpanel\">", (await client.GetAsync($"/Vehicles/Details/{id}?tab=nonsense")).Html);
    }

    [DatabaseFact]
    public async Task Each_dashboard_card_has_Add_fill_up_and_Add_service()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var footer = Regex.Match((await client.GetAsync("/")).Html, @"<div class=""card-footer bg-body"">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Matches($@"href=""/FuelLog/Create\?vehicleId={id}""[^>]*>.*Add fill-up</a>\s*<a class=""btn btn-sm btn-primary"" href=""/Vehicles/{id}/Services/Create""><i class=""bi bi-wrench me-1""></i>Add service</a>", footer);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Vehicles/{id}/Services/Create")).Status);
    }

    [DatabaseFact]
    public async Task Add_service_is_above_the_service_history_card_right_aligned()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var page = (await client.GetAsync($"/Vehicles/Details/{id}")).Html;
        var tab = page[page.IndexOf("id=\"tab-services\"", StringComparison.Ordinal)..];
        // No services yet, so no period toggle on the left.
        Assert.Matches($@"^[^>]*>\s*<div class=""period-toggle-row d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2"">\s*<span></span>\s*<a class=""btn btn-sm btn-primary"" href=""/Vehicles/{id}/Services/Create""><i class=""bi bi-wrench me-1""></i>Add service</a>\s*</div>\s*<div class=""card shadow-sm"">", tab);
        var header = Regex.Match(tab, @"<div class=""card-header bg-body"">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Matches(@"Service history\s*\(0\)", header);
        Assert.DoesNotContain("Services/Create", header);

        // With a service, the toggle takes the left.
        await AddServiceAsync(client, id, "2026-10-01", 45000);
        page = (await client.GetAsync($"/Vehicles/Details/{id}")).Html;
        tab = page[page.IndexOf("id=\"tab-services\"", StringComparison.Ordinal)..];
        Assert.Matches(@"^[^>]*>\s*<div class=""period-toggle-row[^""]*"">\s*<ul class=""nav nav-pills nav-pills-sm", tab);
    }

    [DatabaseFact]
    public async Task The_services_tab_has_the_period_toggle()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var today = Reminders.UkToday();
        await AddServiceAsync(client, id, today.AddMonths(-2).ToString("yyyy-MM-dd"), 45000);
        await AddServiceAsync(client, id, today.AddMonths(-14).ToString("yyyy-MM-dd"), 35000, "Local Garage, a much longer name", "");

        var page = (await client.GetAsync($"/Vehicles/Details/{id}?tab=services")).Html;
        Assert.Matches(@"Service history\s*<span class=""period-stats"" data-period=""12m"">\(1\)</span><span class=""period-stats"" data-period=""all"">\(2\)</span>", page);

        string Rows(string period) => Regex.Match(page, $@"<div class=""period-stats"" data-period=""{period}"">.*?<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        string Sizer(string period) => Regex.Match(page, $@"<div class=""period-stats"" data-period=""{period}"">.*?<tfoot class=""column-sizer""[^>]*>(.*?)</tfoot>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Single(Regex.Matches(Rows("12m"), "<tr>"));
        Assert.Equal(2, Regex.Matches(Rows("all"), "<tr>").Count);
        // Both size their columns from every service, so they line up when switching.
        Assert.Contains("Local Garage, a much longer name", Sizer("12m"));
        Assert.Equal(Sizer("all"), Sizer("12m"));

        // Nothing in the last 12 months.
        await using (var db = Db())
        {
            await db.ServiceRecords.Where(s => s.Mileage == 45000).ExecuteDeleteAsync();
        }
        Assert.Contains("No services in the last 12 months.", (await client.GetAsync($"/Vehicles/Details/{id}?tab=services")).Text);
    }

    [DatabaseFact]
    public async Task An_older_service_doesnt_change_the_next_one()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);

        var older = await AddServiceAsync(client, id, "2025-10-01", 35000);
        Assert.Equal(HttpStatusCode.Redirect, older.Status);
        Assert.Equal((new DateOnly(2027, 10, 1), (int?)55000), await NextDueAsync(id));
        Assert.Contains("Added the service on 01/10/2025.", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);
    }

    [DatabaseFact]
    public async Task Without_an_interval_the_due_date_and_mileage_are_left_alone()
    {
        var id = await AddVehicleAsync(months: 6, miles: null);
        await using (var db = Db())
        {
            (await db.Vehicles.SingleAsync()).ServiceDueMileage = 50000;
            await db.SaveChangesAsync();
        }
        var client = app.ClientFor(UserId);

        await AddServiceAsync(client, id, "2026-10-01", 45000);
        Assert.Equal((new DateOnly(2027, 4, 1), (int?)50000), await NextDueAsync(id));
        Assert.Contains("Next service due 01/04/2027.", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);
    }

    [DatabaseFact]
    public async Task With_no_intervals_its_suggested_to_set_one()
    {
        var id = await AddVehicleAsync(months: null, miles: null);
        var client = app.ClientFor(UserId);
        Assert.Contains("No service interval.", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);

        await AddServiceAsync(client, id, "2026-10-01", 45000);
        Assert.Equal(((DateOnly?)null, (int?)null), await NextDueAsync(id));
        Assert.Contains("Set a service interval on the vehicle to have the next service worked out.", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);
    }

    [DatabaseFact]
    public async Task Editing_the_latest_services_date_or_mileage_moves_the_next_one_but_other_edits_dont()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);
        int serviceId;
        await using (var db = Db()) serviceId = (await db.ServiceRecords.SingleAsync()).Id;

        // Set by hand afterwards: a garage edit keeps it.
        await using (var db = Db())
        {
            (await db.Vehicles.SingleAsync()).ServiceDueDate = new DateOnly(2027, 9, 1);
            await db.SaveChangesAsync();
        }
        var url = $"/Vehicles/{id}/Services/Edit/{serviceId}";
        var saved = await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Service.Garage"] = "Other Garage" });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);
        Assert.Equal((new DateOnly(2027, 9, 1), (int?)55000), await NextDueAsync(id));

        saved = await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Service.Mileage"] = "46000" });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);
        Assert.Equal((new DateOnly(2027, 10, 1), (int?)56000), await NextDueAsync(id));
        Assert.Contains("Service updated. Next service due 01/10/2027 or at 56,000 miles.", (await client.GetAsync($"/Vehicles/Details/{id}")).Text);

        await using var check = Db();
        Assert.Equal(("Other Garage", 46000), await check.ServiceRecords.Select(s => new ValueTuple<string, int>(s.Garage, s.Mileage)).SingleAsync());
    }

    [DatabaseFact]
    public async Task A_conflicting_edit_isnt_saved()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);
        int serviceId;
        await using (var db = Db()) serviceId = (await db.ServiceRecords.SingleAsync()).Id;
        var url = $"/Vehicles/{id}/Services/Edit/{serviceId}";

        var stale = await client.GetAsync(url);
        await client.PostFormAsync(url, await client.GetAsync(url), new() { ["Service.Garage"] = "First" });
        var conflict = await client.PostFormAsync(url, stale, new() { ["Service.Garage"] = "Second" });
        Assert.Equal(HttpStatusCode.OK, conflict.Status);
        Assert.Contains("Someone changed this service after you opened it", conflict.Text);
    }

    [DatabaseFact]
    public async Task Deleting_a_service_leaves_the_next_one_alone()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);
        int serviceId;
        await using (var db = Db()) serviceId = (await db.ServiceRecords.SingleAsync()).Id;

        var url = $"/Vehicles/{id}/Services/Delete/{serviceId}";
        var confirm = await client.GetAsync(url);
        var text = Regex.Replace(Regex.Replace(confirm.Text, "<[^>]+>", ""), @"\s+", " ");
        Assert.Contains("Delete the service for Test Car on 01/10/2026 at 45,000 miles, by Main Dealer (Leeds)?", text);
        var deleted = await client.PostFormAsync(url, confirm);
        Assert.Equal((HttpStatusCode.Redirect, $"/Vehicles/Details/{id}?tab=services"), (deleted.Status, deleted.Location));

        await using var check = Db();
        Assert.Empty(await check.ServiceRecords.ToListAsync());
        Assert.Equal((new DateOnly(2027, 10, 1), (int?)55000), await NextDueAsync(id));
    }

    [DatabaseFact]
    public async Task A_service_is_only_found_under_its_own_vehicle()
    {
        var id = await AddVehicleAsync();
        var otherVehicle = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);
        int serviceId;
        await using (var db = Db()) serviceId = (await db.ServiceRecords.SingleAsync()).Id;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Vehicles/{otherVehicle}/Services/Edit/{serviceId}")).Status);
        var page = await client.GetAsync($"/Vehicles/{id}/Services/Delete/{serviceId}");
        await client.PostFormAsync($"/Vehicles/{otherVehicle}/Services/Delete/{serviceId}", page);
        await using var check = Db();
        Assert.Single(await check.ServiceRecords.ToListAsync());
    }

    [DatabaseFact]
    public async Task Another_users_services_cant_be_seen_or_added_to()
    {
        var id = await AddVehicleAsync();
        await AddServiceAsync(app.ClientFor(UserId), id, "2026-10-01", 45000);
        int serviceId;
        await using (var db = Db()) serviceId = (await db.ServiceRecords.SingleAsync()).Id;

        var other = app.ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Vehicles/{id}/Services/Create")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Vehicles/{id}/Services/Edit/{serviceId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Vehicles/{id}/Services/Delete/{serviceId}")).Status);
    }

    [DatabaseFact]
    public async Task A_service_needs_a_date_mileage_and_garage()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync($"/Vehicles/{id}/Services/Create");
        var invalid = await client.PostFormAsync($"/Vehicles/{id}/Services/Create", form, new()
        {
            ["Service.Date"] = "", ["Service.Mileage"] = "", ["Service.Garage"] = ""
        });
        Assert.Equal(HttpStatusCode.OK, invalid.Status);
        Assert.Contains("The Date field is required.", invalid.Errors);
        Assert.Contains("The Mileage field is required.", invalid.Errors);
        Assert.Contains("The Garage field is required.", invalid.Errors);
    }

    [DatabaseFact]
    public async Task Service_intervals_are_set_on_the_vehicle_form()
    {
        var id = await AddVehicleAsync(null, null);
        var client = app.ClientFor(UserId);
        var edit = await client.GetAsync($"/Vehicles/Edit/{id}");

        var invalid = await client.PostFormAsync($"/Vehicles/Edit/{id}", edit, new() { ["Vehicle.ServiceIntervalMonths"] = "0" });
        Assert.Contains("Service every (months) must be between 1 and 120.", invalid.Errors);

        var saved = await client.PostFormAsync($"/Vehicles/Edit/{id}", edit, new()
        {
            ["Vehicle.ServiceIntervalMonths"] = "24", ["Vehicle.ServiceIntervalMiles"] = "20000"
        });
        Assert.True(saved.Status == HttpStatusCode.Redirect, saved.Errors);
        await using var db = Db();
        var vehicle = await db.Vehicles.SingleAsync();
        Assert.Equal((24, 20000), (vehicle.ServiceIntervalMonths, vehicle.ServiceIntervalMiles));
    }

    [DatabaseFact]
    public async Task Deleting_a_vehicle_deletes_its_services()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);

        var confirm = await client.GetAsync($"/Vehicles/Delete/{id}");
        Assert.Contains("This will also delete its 1 service record(s).", confirm.Text);
        await client.PostFormAsync($"/Vehicles/Delete/{id}", confirm);

        await using var check = Db();
        Assert.Empty(await check.ServiceRecords.ToListAsync());
    }

    [DatabaseFact]
    public async Task The_services_tab_charts_mileage_once_there_are_two_services()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        await AddServiceAsync(client, id, "2026-10-01", 45000);
        var page = (await client.GetAsync($"/Vehicles/Details/{id}?tab=services")).Html;
        Assert.DoesNotContain("data-mileage-chart", page);   // one point isn't a chart
        Assert.DoesNotContain("chart.js@", page);

        await AddServiceAsync(client, id, "2025-10-01", 35000, "Local Garage", "");
        page = (await client.GetAsync($"/Vehicles/Details/{id}?tab=services")).Html;
        Assert.Contains("Mileage at each service", page);
        Assert.Equal(["2025-10-01 35000 Local Garage False", "2026-10-01 45000 Main Dealer, Leeds False"], MileageChart(page));
        Assert.Contains("chart.js@", page);
        Assert.Matches(@"<script src=""/js/mileage-chart[^""]*\.js""", page);

        // A previous owner's service is flagged, for the chart to draw differently.
        await using (var db = Db())
        {
            await db.ServiceRecords.Where(s => s.Mileage == 35000).ExecuteUpdateAsync(u => u.SetProperty(s => s.UnderPreviousOwner, true));
        }
        page = (await client.GetAsync($"/Vehicles/Details/{id}?tab=services")).Html;
        Assert.Equal(["2025-10-01 35000 Local Garage False previous owner", "2026-10-01 45000 Main Dealer, Leeds False"], MileageChart(page));
    }

    /// <summary>The readings in the page's mileage chart, oldest first.</summary>
    internal static List<string> MileageChart(string html)
    {
        var json = WebUtility.HtmlDecode(Regex.Match(html, @"data-mileage-chart=""([^""]*)""").Groups[1].Value);
        return [.. System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(r => $"{r.GetProperty("date").GetString()} {r.GetProperty("miles").GetInt32()} {r.GetProperty("note").GetString()} {r.GetProperty("failed").GetBoolean()}" +
                         (r.GetProperty("previousOwner").GetBoolean() ? " previous owner" : ""))];
    }

    private static async Task<(int? Mileage, string Basis)> EstimateAsync(PageClient client, string page, string date)
    {
        var response = await client.GetResponseAsync($"{page}?handler=Estimate&date={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var mileage = json.GetProperty("mileage");
        return (mileage.ValueKind == System.Text.Json.JsonValueKind.Null ? null : mileage.GetInt32(), json.GetProperty("basis").GetString()!);
    }

    [DatabaseFact]
    public async Task Estimate_works_out_the_mileage_on_the_date_from_the_MOTs_fill_ups_and_other_services()
    {
        var id = await AddVehicleAsync();
        await using (var db = Db())
        {
            var vehicle = await db.Vehicles.SingleAsync(v => v.Id == id);
            vehicle.RegistrationDate = new DateOnly(2020, 3, 1);
            vehicle.MotTests.Add(new Models.MotTest { CompletedDate = new DateTime(2023, 3, 1, 10, 0, 0), Passed = true, Odometer = 30000 });
            await db.SaveChangesAsync();
            (await TestData.AddFillUpAsync(db, id, new DateOnly(2023, 3, 31), ("Shell", "Meadowbank"))).Odometer = 31500;
            await db.SaveChangesAsync();
        }
        var client = app.ClientFor(UserId);
        var create = $"/Vehicles/{id}/Services/Create";

        Assert.Contains("id=\"estimate-mileage\"", (await client.GetAsync(create)).Html);
        Assert.Equal((30500, "Between 30,000 miles at the MOT on 01/03/2023 and 31,500 miles at a fill-up on 31/03/2023."),
            await EstimateAsync(client, create, "2023-03-11"));
        // Before the first MOT: from 0 at first registration.
        Assert.Equal((15014, "Between 0 miles at first registration on 01/03/2020 and 30,000 miles at the MOT on 01/03/2023."),   // 548 of 1,095 days
            await EstimateAsync(client, create, "2021-08-31"));

        // Another service counts, except on its own edit page.
        await AddServiceAsync(client, id, "2023-03-11", 30600);
        Assert.Equal(30600, (await EstimateAsync(client, create, "2023-03-11")).Mileage);
        await using (var db = Db())
        {
            var serviceId = await db.ServiceRecords.Where(s => s.VehicleId == id).Select(s => s.Id).SingleAsync();
            Assert.Equal(30500, (await EstimateAsync(client, $"/Vehicles/{id}/Services/Edit/{serviceId}", "2023-03-11")).Mileage);
        }
    }

    [DatabaseFact]
    public async Task Estimate_says_when_there_is_not_enough_to_go_on_and_only_answers_for_your_own_vehicles()
    {
        var id = await AddVehicleAsync();
        var (mileage, basis) = await EstimateAsync(app.ClientFor(UserId), $"/Vehicles/{id}/Services/Create", "2023-03-11");
        Assert.Null(mileage);
        Assert.StartsWith("There aren't enough odometer readings", basis);
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.ClientFor(NewUser()).GetResponseAsync($"/Vehicles/{id}/Services/Create?handler=Estimate&date=2023-03-11")).StatusCode);
    }

    [DatabaseFact]
    public async Task An_estimated_mileage_is_marked_and_not_used_for_other_estimates()
    {
        var id = await AddVehicleAsync();
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync($"/Vehicles/{id}/Services/Create");
        Assert.Contains("name=\"Service.MileageEstimated\"", form.Html);
        await client.PostFormAsync($"/Vehicles/{id}/Services/Create", form, new()
        {
            ["Service.Date"] = "2023-03-11", ["Service.Mileage"] = "30500", ["Service.Garage"] = "Main Dealer", ["Service.MileageEstimated"] = "true"
        });
        await AddServiceAsync(client, id, "2023-03-01", 30000);
        await AddServiceAsync(client, id, "2023-03-31", 31500);

        Assert.Contains(">Estimated mileage</span>", (await client.GetAsync($"/Vehicles/Details/{id}?tab=services")).Html);
        // Only the recorded services count: the estimate on 11/03 isn't a reading.
        Assert.Equal((30500, "Between 30,000 miles at a service on 01/03/2023 and 31,500 miles at a service on 31/03/2023."),
            await EstimateAsync(client, $"/Vehicles/{id}/Services/Create", "2023-03-11"));
        await using var db = Db();
        Assert.Equal([false, true, false], await db.ServiceRecords.Where(s => s.VehicleId == id).OrderBy(s => s.Date).Select(s => s.MileageEstimated).ToListAsync());
    }
}
