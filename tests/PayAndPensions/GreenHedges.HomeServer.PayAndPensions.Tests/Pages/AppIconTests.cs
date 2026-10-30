using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

/// <summary>The icons and web app manifest: browser tabs, bookmarks and phone home screens.</summary>
public class AppIconTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Every_page_links_the_icons_and_the_manifest()
    {
        var page = await app.ClientFor(null).GetAsync("/Account/SignedOut");

        // Fingerprinted by the static assets (e.g. /favicon.f33cswhs8m.ico), so a changed icon gets a new address.
        Assert.Matches(@"<link rel=""icon"" href=""/favicon\.\w+\.ico""", page.Html);
        Assert.Matches(@"<link rel=""icon"" href=""/favicon\.\w+\.svg"" type=""image/svg\+xml""", page.Html);
        Assert.Matches(@"<link rel=""apple-touch-icon"" href=""/icons/apple-touch-icon\.\w+\.png""", page.Html);
        Assert.Matches(@"<link rel=""manifest"" href=""/site\.\w+\.webmanifest""", page.Html);
        Assert.Contains("<meta name=\"apple-mobile-web-app-title\" content=\"Pay\"", page.Html);

        // The linked (fingerprinted) addresses are served too, without signing in.
        foreach (var href in System.Text.RegularExpressions.Regex.Matches(page.Html, @"<link rel=""(?:icon|apple-touch-icon|manifest)"" href=""([^""]+)""")
                     .Select(m => m.Groups[1].Value))
            Assert.Equal(HttpStatusCode.OK, (await app.ClientFor(null).GetResponseAsync(href)).StatusCode);
    }

    // Phones and browsers fetch these on their own, without the sign-in cookie: they mustn't need signing in.
    [Theory]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/icons/apple-touch-icon.png", "image/png")]
    [InlineData("/site.webmanifest", "application/manifest+json")]
    public async Task Icons_and_the_manifest_are_served_without_signing_in(string url, string type)
    {
        var response = await app.ClientFor(null).GetResponseAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(type, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_apple_touch_icon_is_180_pixels_square_with_no_transparency()
    {
        var png = await GetBytesAsync("/icons/apple-touch-icon.png");

        Assert.Equal((180, 180), PngSize(png));
        Assert.Equal(2, png[25]); // colour type 2: RGB, no alpha (iOS shows transparency as black)
    }

    [Fact]
    public async Task The_manifest_opens_standalone_and_its_icons_exist_at_their_sizes()
    {
        using var manifest = JsonDocument.Parse(await GetBytesAsync("/site.webmanifest"));
        var root = manifest.RootElement;
        Assert.Equal("Pay & Pensions", root.GetProperty("name").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");
        foreach (var icon in icons)
        {
            var size = int.Parse(icon.GetProperty("sizes").GetString()!.Split('x')[0]);
            Assert.Equal((size, size), PngSize(await GetBytesAsync(icon.GetProperty("src").GetString()!)));
        }
    }

    private async Task<byte[]> GetBytesAsync(string url)
    {
        var response = await app.ClientFor(null).GetResponseAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>A PNG's width and height, from its IHDR chunk.</summary>
    private static (int Width, int Height) PngSize(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }
}
