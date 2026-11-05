using System.Net;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class LayoutTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task The_theme_choice_is_a_submenu_of_the_user_menu()
    {
        var page = (await app.ClientFor(UserId).GetAsync("/")).Html;
        var nav = Regex.Match(page, "<nav .*?</nav>", RegexOptions.Singleline).Value;

        // The account menu stays open for clicks inside, so Theme can expand in place.
        Assert.Matches(@"data-bs-toggle=""dropdown"" data-bs-auto-close=""outside"" data-bs-display=""static""[^>]*aria-label=""Account""", nav);
        // Left-aligned on a phone, where the collapsed menu bar puts the button on the left; right-aligned otherwise.
        Assert.Matches(@"aria-label=""Account""[^>]*>\s*<i class=""bi bi-person-circle""></i>\s*</button>\s*<ul class=""dropdown-menu dropdown-menu-sm-end"">", nav);
        var account = nav[nav.IndexOf("aria-label=\"Account\"", StringComparison.Ordinal)..];
        Assert.Matches(@"data-bs-toggle=""collapse""\s+data-bs-target=""#theme-options""[^>]*>\s*<i class=""bi bi-circle-half me-2"" id=""theme-icon""></i>Theme", account);
        var options = Regex.Match(account, @"<li class=""collapse"" id=""theme-options"">(.*?)</li>\s*<li><hr", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(["light", "dark", "auto"], Regex.Matches(options, @"data-theme-value=""(\w+)""").Select(m => m.Groups[1].Value));

        // Nowhere else: no separate theme button in the menu bar.
        Assert.Equal(3, Regex.Matches(nav, "data-theme-value=").Count);
        Assert.DoesNotContain("Colour theme", nav);
    }

    [DatabaseFact]
    public async Task Cancel_and_Back_in_forms_are_buttons_not_links()
    {
        int vehicleId;
        await using (var db = Db()) vehicleId = (await TestData.AddVehicleAsync(db)).Id;
        var client = app.ClientFor(UserId);
        foreach (var url in new[]
                 {
                     "/Owners/Create", "/Vehicles/Create", $"/Vehicles/Edit/{vehicleId}", $"/Vehicles/Delete/{vehicleId}",
                     $"/FuelLog/Create?vehicleId={vehicleId}", $"/Vehicles/{vehicleId}/Services/Create"
                 })
        {
            var html = (await client.GetAsync(url)).Html;
            var links = Regex.Matches(html, @"<a [^>]*>(?:Cancel|Back)</a>").Select(m => m.Value).ToList();
            Assert.NotEmpty(links);
            Assert.All(links, link => Assert.Contains("class=\"btn btn-outline-secondary\"", link));
        }
    }

    [DatabaseFact]
    public async Task Manage_menu_holds_the_list_pages_and_each_link_loads()
    {
        var client = app.ClientFor(UserId);
        var page = await client.GetAsync("/");
        var menu = Regex.Match(page.Html, @"dropdown-toggle[^>]*>Manage</button>\s*<ul class=""dropdown-menu"">(.*?)</ul>", RegexOptions.Singleline);
        Assert.True(menu.Success, "No Manage menu");

        var items = Regex.Matches(menu.Groups[1].Value, "<a [^>]*>([^<]+)</a>").Select(m => (Text: m.Groups[1].Value, Href: PageResponse.Href(m.Value))).ToList();
        Assert.Equal(["Vehicles", "Owners", "Fuel Brands", "Import", "Backup", "Restore"], items.Select(i => i.Text));
        Assert.Equal("/FuelBrands", items.Single(i => i.Text == "Fuel Brands").Href);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Brands")).Status);   // the old address is gone
        foreach (var (text, href) in items)
            Assert.True(href is not null && (await client.GetAsync(href)).Status == HttpStatusCode.OK, $"{text} ({href}) doesn't load");

        // Only Dashboard, Fuel log and Insurance stay as top-level links.
        var topLevel = Regex.Matches(page.Html, @"<a class=""nav-link""[^>]*>([^<]+)</a>").Select(m => m.Groups[1].Value);
        Assert.Equal(["Dashboard", "Fuel Log", "Insurance"], topLevel);
        Assert.Matches(@"<a class=""nav-link"" href=""/FuelLog"">Fuel Log</a>", page.Html);
    }

    [DatabaseFact]
    public async Task Table_column_headers_are_title_case()
    {
        int vehicleId;
        await using (var db = Db())
        {
            vehicleId = (await TestData.AddVehicleAsync(db)).Id;
            await TestData.AddFillUpAsync(db, vehicleId, new DateOnly(2026, 1, 1), ("Shell", "Meadowbank"));
        }
        var client = app.ClientFor(UserId);
        var brands = await client.GetAsync("/FuelBrands");
        var pages = new[] { "/", "/FuelLog", $"/Vehicles/Details/{vehicleId}", "/Vehicles", "/Owners", "/FuelBrands", brands.RowLink("Shell", "Stations")! };

        foreach (var url in pages)
        {
            var headers = Regex.Matches((await client.GetAsync(url)).Text, "<th[^>]*>([^<]+)</th>").Select(m => m.Groups[1].Value.Trim()).ToList();
            Assert.True(headers.Count > 0, $"No table on {url}");
            foreach (var header in headers)
            {
                // Every word capitalised, including after "-" and "/" ("Fill-Ups", "£/Litre"); "p" (pence) is a unit.
                var words = Regex.Split(header, @"[\s/-]+").Where(w => w.Length > 0 && char.IsLetter(w[0]) && w != "p");
                Assert.True(words.All(w => char.IsUpper(w[0])), $"\"{header}\" on {url} isn't title case");
            }
        }
    }
}
