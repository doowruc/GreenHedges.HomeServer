using System.Net;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class SettingsPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Settings_is_in_the_user_menu()
    {
        var page = await app.ClientFor(UserId).GetAsync("/");
        var menu = Regex.Match(page.Html, @"aria-label=""Account"".*?<ul[^>]*>(.*?)</ul>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Matches(@"<a class=""dropdown-item"" href=""/Settings"">.*Settings</a>", menu);
    }

    [DatabaseFact]
    public async Task Saving_and_clearing_the_reminder_email()
    {
        var client = app.ClientFor(UserId);
        var form = await client.GetAsync("/Settings");
        // Backups are no longer emailed.
        Assert.DoesNotContain("Scheduled backups", form.Text);
        Assert.DoesNotContain("Settings.BackupEmail", form.Html);
        Assert.DoesNotContain("Email a backup now", form.Text);

        var invalid = await client.PostFormAsync("/Settings", form, new() { ["Settings.ReminderEmail"] = "not an address" });
        Assert.Equal(HttpStatusCode.OK, invalid.Status);
        Assert.Contains("not a valid e-mail address", invalid.Errors);

        var saved = await client.PostFormAsync("/Settings", form, new() { ["Settings.ReminderEmail"] = " me@tests.invalid " });
        Assert.Equal((HttpStatusCode.Redirect, "/Settings"), (saved.Status, saved.Location));
        form = await client.GetAsync("/Settings");
        Assert.Contains("value=\"me@tests.invalid\"", form.Html);

        // Saved again from the reloaded form, so its row version is current.
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/Settings", form, new() { ["Settings.ReminderEmail"] = "" })).Status);
        Assert.DoesNotContain("me@tests.invalid", (await client.GetAsync("/Settings")).Html);
    }

    [DatabaseFact]
    public async Task Settings_belong_to_each_user()
    {
        var client = app.ClientFor(UserId);
        await client.PostFormAsync("/Settings", await client.GetAsync("/Settings"), new() { ["Settings.ReminderEmail"] = "mine@tests.invalid" });
        Assert.DoesNotContain("mine@tests.invalid", (await app.ClientFor(NewUser()).GetAsync("/Settings")).Html);
    }
}
