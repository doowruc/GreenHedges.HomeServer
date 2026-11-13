using System.Net;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Presents.Tests.Pages;

public class SecurityTests(AppFactory app) : IClassFixture<AppFactory>
{
    [DatabaseTheory]
    [InlineData("/")]
    [InlineData("/Children")]
    [InlineData("/Children/Create")]
    [InlineData("/Children/Edit/1")]
    [InlineData("/Children/Delete/1")]
    [InlineData("/2025")]
    [InlineData("/2025/Edit")]
    [InlineData("/2025/Delete")]
    [InlineData("/Purchases/Edit/1")]
    [InlineData("/Purchases/Delete/1")]
    public async Task Pages_need_a_signed_in_user(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientFor(null).GetAsync(url)).Status);

    [DatabaseTheory]
    [InlineData("/Account/SignedOut")]
    [InlineData("/health")]
    public async Task Some_pages_are_open_to_anyone(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await app.ClientFor(null).GetAsync(url)).Status);
}
