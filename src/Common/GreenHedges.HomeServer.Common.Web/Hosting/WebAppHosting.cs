using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace GreenHedges.HomeServer.Common.Web.Hosting;

/// <summary>Setup every web app on the server shares: required settings, running behind Caddy, lasting keys.</summary>
public static class WebAppHosting
{
    /// <summary>
    /// A setting the app can't run without. In development they live in user secrets, elsewhere in environment
    /// variables (e.g. <c>Auth0__Domain</c>).
    /// </summary>
    public static string RequiredSetting(this IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"{key} is not configured. Set it with: dotnet user-secrets set \"{key}\" \"<value>\"");

    /// <summary>
    /// On the server the app sits behind Caddy, which terminates HTTPS and passes requests on from 127.0.0.1
    /// with X-Forwarded-Proto and X-Forwarded-For (the Host header comes through as it was). Trusting them, from loopback
    /// proxies only (ASP.NET's default), gives Auth0 the https:// callback and keeps HTTPS redirection quiet.
    /// The app calls <c>UseForwardedHeaders()</c> first in its pipeline.
    /// </summary>
    public static void AddForwardedHeaders(this WebApplicationBuilder builder) =>
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

    /// <summary>
    /// Data-protection keys protect the sign-in cookie and antiforgery tokens, so they must outlive restarts and deploys.
    /// On the server they go in the data folder (DataProtection:KeysFolder; a relative path is under the working
    /// directory); otherwise ASP.NET's default (the user profile) applies.
    /// </summary>
    public static void AddLastingDataProtection(this WebApplicationBuilder builder, string applicationName)
    {
        if (builder.Configuration["DataProtection:KeysFolder"] is { Length: > 0 } keysFolder)
        {
            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(keysFolder)))
                .SetApplicationName(applicationName);
        }
    }
}
