using System.Security.Claims;

namespace GreenHedges.HomeServer.Common.Web.Auth;

/// <summary>
/// The email addresses allowed to use the app. Signing in to Auth0 isn't enough on its own:
/// the account's email must be verified and on this list.
/// </summary>
public class AllowedUsers(IEnumerable<string> emails)
{
    public const string PolicyName = "AllowedUser";

    private readonly HashSet<string> _emails = new(emails, StringComparer.OrdinalIgnoreCase);

    public int Count => _emails.Count;

    /// <summary>Parses a setting such as <c>"me@example.com; you@example.com"</c> (commas work too).</summary>
    public static AllowedUsers FromSetting(string setting) =>
        new(setting.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string? Email(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;

    public static bool EmailVerified(ClaimsPrincipal user) =>
        string.Equals(user.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

    public bool IsAllowed(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        EmailVerified(user) &&
        Email(user) is { } email &&
        _emails.Contains(email);
}
