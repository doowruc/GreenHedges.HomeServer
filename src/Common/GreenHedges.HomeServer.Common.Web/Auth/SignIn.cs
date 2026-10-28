using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using GreenHedges.HomeServer.Common.Web.Hosting;

namespace GreenHedges.HomeServer.Common.Web.Auth;

public static class SignIn
{
    /// <summary>
    /// Auth0 sign-in (settings <c>Auth0:Domain</c>, <c>ClientId</c>, <c>ClientSecret</c>), and a policy that every page
    /// needs unless it opts out with <c>[AllowAnonymous]</c>: a signed-in user whose verified email is in
    /// <c>AllowedEmails</c>. Signed-in users who aren't allowed are sent to Account/AccessDenied. Also registers
    /// <see cref="AllowedUsers"/> and <see cref="CurrentUser"/>.
    /// </summary>
    /// <param name="onTokenValidated">Runs on each sign-in, e.g. to hand existing data to the user.</param>
    public static void AddAllowedUserSignIn(
        this WebApplicationBuilder builder, Func<TokenValidatedContext, Task>? onTokenValidated = null)
    {
        builder.Services.AddAuth0WebAppAuthentication(options =>
        {
            options.Domain = builder.Configuration.RequiredSetting("Auth0:Domain");
            options.ClientId = builder.Configuration.RequiredSetting("Auth0:ClientId");
            options.ClientSecret = builder.Configuration.RequiredSetting("Auth0:ClientSecret");
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.Scope = "openid profile email";
            if (onTokenValidated is not null)
                options.OpenIdConnectEvents = new OpenIdConnectEvents { OnTokenValidated = onTokenValidated };
        });

        var allowedUsers = AllowedUsers.FromSetting(builder.Configuration.RequiredSetting("AllowedEmails"));
        builder.Services.AddSingleton(allowedUsers);
        var allowedUserPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => allowedUsers.IsAllowed(context.User))
            .Build();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AllowedUsers.PolicyName, allowedUserPolicy)
            .SetFallbackPolicy(allowedUserPolicy);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<CurrentUser>();
    }
}
