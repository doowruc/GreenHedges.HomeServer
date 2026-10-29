using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GreenHedges.HomeServer.Common.Alerts;
using CommonBackupOptions = GreenHedges.HomeServer.Common.Backups.BackupOptions;
using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Backups;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Common.Web.Hosting;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Monitoring;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.Services.Import;

var builder = WebApplication.CreateBuilder(args);

// Under systemd (Type=notify) on the server: tells systemd when it's ready and logs plainly to the journal. Does
// nothing elsewhere.
builder.Services.AddSystemd();

// Logs, traces and metrics over OTLP when an endpoint is set: on the server, to Grafana (deploy/telemetry/).
builder.AddTelemetry("PayAndPensions");

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
builder.Services.AddScoped<PayHistory>();
builder.Services.AddScoped<PayslipImporter>();

// Alert emails (through Resend) and the off-machine heartbeat (healthchecks.io), shared with the other services
// (GreenHedges.HomeServer.Common). Both are off until configured: Alerts:ResendApiKey, From and To; Heartbeat:PingUrl.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new CollectorIdentity("PayAndPensions"));
builder.AddAlerting();
builder.AddHeartbeat();
builder.Services.AddOptions<PayAndPensionsAlertOptions>()
    .BindConfiguration(AlertOptions.SectionName)
    .Validate(o => o.DatabaseDownAfter > TimeSpan.Zero, "Alerts:DatabaseDownAfter must be positive.")
    .ValidateOnStart();
// Nightly database backups (03:45, after Vehicle Manager's), gzipped and kept 7, with age-encrypted off-site copies,
// through Common as the other services. Off by default (Backup:Enabled): on the server, where SQL Server shares a folder
// with the app. The database is ConnectionStrings:PayAndPensions (the CollectorIdentity's name).
builder.Services.AddOptions<SqlServerBackupOptions>()
    .BindConfiguration(CommonBackupOptions.SectionName)
    .Validate(o => !builder.Configuration.GetValue<bool>("Backup:Enabled") || !string.IsNullOrWhiteSpace(o.SqlServerFolder),
        "Backup:SqlServerFolder must be set when Backup:Enabled is true.")
    .ValidateOnStart();
builder.AddBackups<SqlServerBackupSource>();

builder.Services.AddSingleton<AppHealth>();
builder.Services.AddSingleton<IHeartbeatSource>(services => services.GetRequiredService<AppHealth>());
builder.Services.AddSingleton<IDatabaseProbe, DatabaseProbe<AppDbContext>>();
// The monitor probes the database every minute. On by default outside Development; Alerts:Monitor overrides it.
if (builder.Configuration.GetValue("Alerts:Monitor", !builder.Environment.IsDevelopment()))
    builder.Services.AddHostedService<PayAndPensionsAlertMonitor>();

var app = builder.Build();

// `--test-alert` sends one alert through the configured sender and exits, to check the Resend setup.
if (await app.TrySendTestAlertAsync(args))
    return;

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
