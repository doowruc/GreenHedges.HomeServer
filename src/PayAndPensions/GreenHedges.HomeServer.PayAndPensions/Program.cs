using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Common.Web.Hosting;
using GreenHedges.HomeServer.PayAndPensions.Data;

var builder = WebApplication.CreateBuilder(args);

// Under systemd (Type=notify) on the server: tells systemd when it's ready and logs plainly to the journal. Does
// nothing elsewhere.
builder.Services.AddSystemd();

// Auth0 sign-in, and every page requires a signed-in user whose verified email is on the allow-list (AllowedEmails),
// unless it opts out with [AllowAnonymous]. Signed-in users who aren't allowed are sent to Account/AccessDenied.
// Settings live in user secrets in development (see the README), environment variables elsewhere, e.g. Auth0__Domain.
builder.AddAllowedUserSignIn();

// Behind Caddy on the server, with sign-in cookies that outlive restarts and deploys (Common.Web's WebAppHosting).
builder.AddForwardedHeaders();
builder.AddLastingDataProtection("PayAndPensions");

// /health: "Healthy" (200) when the database answers, else "Unhealthy" (503). For the heartbeat and for checking by hand.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck<AppDbContext>>("database", timeout: TimeSpan.FromSeconds(10));

builder.Services.AddRazorPages();
// SQL Server: the server's shared instance, or the local container in development (see the README).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.RequiredSetting("ConnectionStrings:PayAndPensions"),
        sql => sql.EnableRetryOnFailure()));

var app = builder.Build();

// Create the tables on first run and apply any pending migrations. The tests turn this off, so
// only the real app changes the database's schema.
if (app.Configuration.GetValue("MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.MigrateDatabase(app.Logger);
}

// First, so everything after sees the original scheme and client address.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// UK formats: dd/MM/yyyy dates and £ currency.
var ukCulture = new CultureInfo("en-GB");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(ukCulture),
    SupportedCultures = [ukCulture],
    SupportedUICultures = [ukCulture]
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapRazorPages().WithStaticAssets();

app.Run();

// Lets tests start the app in memory (WebApplicationFactory<Program>).
public partial class Program;
