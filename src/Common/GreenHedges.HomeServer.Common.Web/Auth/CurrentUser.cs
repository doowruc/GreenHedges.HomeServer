using System.Security.Claims;

namespace GreenHedges.HomeServer.Common.Web.Auth;

/// <summary>The signed-in user for the current request, identified by their Auth0 user id.</summary>
public class CurrentUser(IHttpContextAccessor httpContextAccessor)
{
    private string? _actingAs;

    /// <summary>The current user's id, or null outside a signed-in request (unless <see cref="ActAs"/> was called).</summary>
    public virtual string? Id => _actingAs ?? IdOf(httpContextAccessor.HttpContext?.User);

    /// <summary>
    /// Makes background work (outside a request, e.g. a scheduled backup) act as <paramref name="userId"/>
    /// for the rest of this scope, so the per-user query filters apply to them.
    /// </summary>
    public void ActAs(string userId)
    {
        if (httpContextAccessor.HttpContext is not null)
            throw new InvalidOperationException("Only background work can act as another user.");
        _actingAs = userId;
    }

    /// <summary>The Auth0 user id (<c>sub</c> claim), which ASP.NET maps to <see cref="ClaimTypes.NameIdentifier"/>.</summary>
    public static string? IdOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? (user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub"))?.Value
            : null;
}
