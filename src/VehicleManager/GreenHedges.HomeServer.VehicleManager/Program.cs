using System.Globalization;
using Coravel;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Resend;
using GreenHedges.HomeServer.Common.Alerts;
using CommonBackupOptions = GreenHedges.HomeServer.Common.Backups.BackupOptions;
using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Backups;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Common.Web.Hosting;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Monitoring;
using GreenHedges.HomeServer.VehicleManager.Pages.FuelBrands;
using GreenHedges.HomeServer.VehicleManager.Services;

var builder = WebApplication.CreateBuilder(args);

// Under systemd (Type=notify) on the server: tells systemd when it's ready and logs plainly to the journal. Does
// nothing elsewhere.
builder.Services.AddSystemd();

// Logs, traces and metrics over OTLP when an endpoint is set: on the server, to Grafana (deploy/telemetry/).
builder.AddTelemetry();

// Settings live in user secrets in development (see README), environment variables elsewhere, e.g. Auth0__Domain.
string RequiredSetting(string key) => builder.Configuration.RequiredSetting(key);

// Auth0 sign-in, and every page requires a signed-in user whose verified email is on the allow-list (AllowedEmails),
// unless it opts out with [AllowAnonymous]. Signed-in users who aren't allowed are sent to Account/AccessDenied.
builder.AddAllowedUserSignIn(
    // On sign-in, hand any data from before per-user data to the ExistingDataOwner (see README).
    context => context.HttpContext.RequestServices
        .GetRequiredService<ExistingDataClaim>()
        .ClaimIfOwnerAsync(context.Principal!));

// Behind Caddy on the server, with sign-in cookies that outlive restarts and deploys (Common.Web's WebAppHosting).
builder.AddForwardedHeaders();
builder.AddLastingDataProtection("VehicleManager");

// /health: "Healthy" (200) when the database answers, else "Unhealthy" (503). For the heartbeat and for checking by hand.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck<AppDbContext>>("database", timeout: TimeSpan.FromSeconds(10));

builder.Services.AddRazorPages();
builder.Services.AddScoped<ExistingDataClaim>();
builder.Services.AddScoped<FuelStops>();
builder.Services.AddScoped<FuelBrandList>();
builder.Services.AddScoped<FuelStationList>();
// Azure SQL Database (see README). Retries cover Azure's brief transient faults, and a serverless
// database waking up from auto-pause.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(RequiredSetting("ConnectionStrings:VehicleManager"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddScoped<FuelStatsService>();
builder.Services.AddScoped<FuelImportService>();
builder.Services.AddScoped<BackupService>();

// Backups are emailed through Resend (see README). The sender's domain must be verified in Resend.
builder.Services.AddResend(options =>
{
    options.ApiToken = RequiredSetting("Resend:ApiKey");
    options.ThrowExceptions = true;
});
builder.Services.AddSingleton(new EmailOptions(RequiredSetting("Resend:From")));
builder.Services.AddScoped<BackupEmailer>();
builder.Services.AddScoped<ReminderEmailer>();

// MOT and tax lookups (see README). Both are optional: without their settings, those dates are typed in.
var dvlaOptions = DvlaOptions.From(builder.Configuration.GetSection("Dvla"));
builder.Services.AddSingleton(dvlaOptions);
builder.Services.AddHttpClient<DvlaClient>(http =>
{
    http.BaseAddress = new Uri(dvlaOptions.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(20);
});
var motHistoryOptions = MotHistoryOptions.From(builder.Configuration.GetSection("MotHistory"));
builder.Services.AddSingleton(motHistoryOptions);
builder.Services.AddSingleton<MotHistoryTokenCache>();
builder.Services.AddHttpClient<MotHistoryClient>(http =>
{
    http.BaseAddress = new Uri(motHistoryOptions.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<VehicleLookup>();

// Alert emails (through Resend) and the off-machine heartbeat (healthchecks.io), shared with the collectors
// (GreenHedges.HomeServer.Common). Both are off until configured: Alerts:ResendApiKey, From and To; Heartbeat:PingUrl.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new CollectorIdentity("VehicleManager"));
builder.AddAlerting();
builder.AddHeartbeat();
builder.Services.AddOptions<VehicleManagerAlertOptions>()
    .BindConfiguration(AlertOptions.SectionName)
    .Validate(o => o.DatabaseDownAfter > TimeSpan.Zero && o.JobOverdueAfter > TimeSpan.Zero,
        "Alerts:DatabaseDownAfter and Alerts:JobOverdueAfter must be positive.")
    .ValidateOnStart();
// Nightly database backups (03:30), gzipped and kept 7, with age-encrypted off-site copies, through Common as for the
// collectors. Off by default (Backup:Enabled): on the server, where SQL Server shares a folder with the app.
builder.Services.AddOptions<SqlServerBackupOptions>()
    .BindConfiguration(CommonBackupOptions.SectionName)
    .Validate(o => !builder.Configuration.GetValue<bool>("Backup:Enabled") || !string.IsNullOrWhiteSpace(o.SqlServerFolder),
        "Backup:SqlServerFolder must be set when Backup:Enabled is true.")
    .ValidateOnStart();
builder.AddBackups<SqlServerBackupSource>();

builder.Services.AddSingleton<AppHealth>();
builder.Services.AddSingleton<IHeartbeatSource>(services => services.GetRequiredService<AppHealth>());
builder.Services.AddSingleton<IDatabaseProbe, DatabaseProbe<AppDbContext>>();
// The monitor probes the database every minute. On by default outside Development; off in Development, so a local
// copy left running doesn't keep a pausing database (Azure SQL's free offer) awake. Alerts:Monitor overrides it.
if (builder.Configuration.GetValue("Alerts:Monitor", !builder.Environment.IsDevelopment()))
    builder.Services.AddHostedService<VehicleManagerAlertMonitor>();

builder.Services.AddScheduler();
builder.Services.AddTransient<ScheduledBackups>();
builder.Services.AddTransient<ScheduledReminders>();

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

app.Services.UseScheduler(scheduler =>
    {
        // Nightly, email each user a backup if their data has changed since their last one.
        scheduler.Schedule<ScheduledBackups>()
            .DailyAt(1, 0)
            .Zoned(TimeZoneInfo.Utc)
            .PreventOverlapping(nameof(ScheduledBackups));
        // Each morning (UK time), update MOT and tax dates and email any reminders due.
        scheduler.Schedule<ScheduledReminders>()
            .DailyAt(7, 0)
            .Zoned(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"))
            .PreventOverlapping(nameof(ScheduledReminders));
    })
    .OnError(ex => app.Logger.LogError(ex, "A scheduled task failed"));

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
