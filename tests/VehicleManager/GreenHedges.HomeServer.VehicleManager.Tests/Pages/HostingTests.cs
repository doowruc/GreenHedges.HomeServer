using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.VehicleManager.Monitoring;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

/// <summary>What running on the server needs: health, forwarded headers, lasting keys, alerts and the heartbeat.</summary>
public class HostingTests(AppFactory app) : IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task Health_answers_without_signing_in()
    {
        var response = await app.ClientFor(null).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Healthy", response.Html);
    }

    [Theory]
    [InlineData("127.0.0.1", "https")] // Caddy, on the same machine
    [InlineData("::1", "https")]
    [InlineData("192.0.2.10", "http")] // anyone else's X-Forwarded-Proto is ignored
    public async Task Forwarded_headers_are_trusted_only_from_the_local_proxy(string proxy, string scheme)
    {
        var context = await app.Server.SendAsync(c =>
        {
            c.Request.Method = "GET";
            c.Request.Path = "/Account/SignedOut";
            c.Request.Headers["X-Forwarded-Proto"] = "https";
            c.Request.Headers["X-Forwarded-For"] = "100.64.0.7";
            c.Connection.RemoteIpAddress = IPAddress.Parse(proxy);
        });

        Assert.Equal(scheme, context.Request.Scheme);
    }

    [Fact]
    public void Data_protection_keys_go_in_the_configured_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"vm-keys-{Guid.NewGuid():N}");
        try
        {
            using var configured = app.WithWebHostBuilder(b => b.UseSetting("DataProtection:KeysFolder", folder));
            configured.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("tests").Protect("x");

            Assert.NotEmpty(Directory.GetFiles(folder, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Alerts_and_the_heartbeat_are_wired_up()
    {
        Assert.IsType<AppHealth>(app.Services.GetRequiredService<IHeartbeatSource>());
        Assert.Contains(app.Services.GetServices<IHostedService>(), s => s is VehicleManagerAlertMonitor);
        Assert.IsType<LoggingAlertSender>(app.Services.GetRequiredService<IAlertSender>()); // no Resend key in tests
        Assert.NotNull(app.Services.GetRequiredService<AlertBook>());
    }
}
