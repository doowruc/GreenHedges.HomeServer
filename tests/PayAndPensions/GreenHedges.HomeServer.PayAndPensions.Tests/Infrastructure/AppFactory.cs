using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>
/// Runs the app in memory against the tests' throwaway database (<see cref="TestDatabase"/>). Auth0 is replaced by a test sign-in: a
/// request is signed in as the user named in its <see cref="UserHeader"/> (see <see cref="ClientFor"/>),
/// with an allowed, verified email; a request without it isn't signed in. The app doesn't migrate at startup
/// (<see cref="TestDatabase"/> already has).
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string UserHeader = "X-Test-User";
    public const string Email = "tests@payandpensions.invalid";

    static AppFactory()
    {
        // Environment variables override the app's user secrets. Auth0 is never called (the test
        // sign-in replaces it), but the app needs settings to start.
        Environment.SetEnvironmentVariable("AllowedEmails", Email);
        Environment.SetEnvironmentVariable("Auth0__Domain", "tests.invalid");
        Environment.SetEnvironmentVariable("Auth0__ClientId", "tests");
        Environment.SetEnvironmentVariable("Auth0__ClientSecret", "tests");

        // The app uses the tests' throwaway database (created and migrated by TestDatabase), never its own from user
        // secrets. Without a test SQL Server it gets a name that can't connect, so it can't reach real data either.
        Environment.SetEnvironmentVariable("ConnectionStrings__PayAndPensions",
            TestDatabase.IsConfigured ? TestDatabase.ConnectionString : "Server=tests.invalid;Database=none;Connect Timeout=1");
        Environment.SetEnvironmentVariable("MigrateOnStartup", "false");

        // The alert monitor runs (it's off by default in Development), but sends no email and no heartbeat pings
        // (neither is configured), and keeps open alerts out of the working folder.
        Environment.SetEnvironmentVariable("Alerts__Monitor", "true");
        Environment.SetEnvironmentVariable("Alerts__StateFile",
            Path.Combine(Path.GetTempPath(), $"payandpensions-tests-alerts-{Guid.NewGuid():N}.json"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestSignIn>(TestSignIn.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = o.DefaultForbidScheme = TestSignIn.SchemeName;
            });
        });
    }

    /// <summary>A client signed in as <paramref name="userId"/>, or not signed in if null. Doesn't follow redirects.</summary>
    public PageClient ClientFor(string? userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userId is not null) client.DefaultRequestHeaders.Add(UserHeader, userId);
        return new PageClient(client);
    }

    private sealed class TestSignIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim("email", Email),
                new Claim("email_verified", "true")
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
