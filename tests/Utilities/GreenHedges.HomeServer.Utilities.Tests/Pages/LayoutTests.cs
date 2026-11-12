using System.Net;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Utilities.Tests.Pages;

/// <summary>What every page shares: the navbar and Bootstrap tooltips.</summary>
public class LayoutTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Titles_become_Bootstrap_tooltips() =>
        Assert.Matches("<script src=\"/js/tooltips[^\"]*\\.js", (await app.ClientFor(UserId).GetAsync("/")).Html);

    [DatabaseFact]
    public async Task Signed_in_pages_have_the_menu_and_signed_out_pages_dont()
    {
        var page = await app.ClientFor(UserId).GetAsync("/");
        Assert.Contains(">Dashboard</a>", page.Html);
        foreach (var utility in new[] { "Electricity", "Gas", "Water" })
            Assert.Contains($"<a class=\"nav-link\" href=\"/{utility}\">{utility}</a>", page.Html);

        var signedOut = await app.ClientFor(null).GetAsync("/Account/SignedOut");
        Assert.Equal(HttpStatusCode.OK, signedOut.Status);
        Assert.DoesNotContain(">Dashboard</a>", signedOut.Html);
    }
}
