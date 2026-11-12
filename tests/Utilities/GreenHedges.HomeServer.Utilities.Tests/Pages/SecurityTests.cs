using System.Net;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Utilities.Tests.Pages;

public class SecurityTests(AppFactory app) : IClassFixture<AppFactory>
{
    [DatabaseTheory]
    [InlineData("/")]
    public async Task Pages_need_a_signed_in_user(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientFor(null).GetAsync(url)).Status);

    [DatabaseTheory]
    [InlineData("/Account/SignedOut")]
    [InlineData("/health")]
    public async Task Some_pages_are_open_to_anyone(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await app.ClientFor(null).GetAsync(url)).Status);
}
