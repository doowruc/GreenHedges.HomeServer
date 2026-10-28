using System.Security.Claims;
using GreenHedges.HomeServer.Common.Web.Auth;

namespace GreenHedges.HomeServer.Common.Web.Tests;

public class AllowedUsersTests
{
    private static readonly AllowedUsers List = AllowedUsers.FromSetting(" me@example.com ; Partner@Example.com,, ");

    private static ClaimsPrincipal User(bool signedIn, params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), signedIn ? "test" : null));

    [Fact]
    public void Parses_semicolons_and_commas() => Assert.Equal(2, List.Count);

    [Theory]
    [InlineData("email", "me@example.com", "true", true)]
    [InlineData(ClaimTypes.Email, "me@example.com", "true", true)]
    [InlineData("email", "PARTNER@example.COM", "True", true)]       // case doesn't matter
    [InlineData("email", "me@example.com", "false", false)]          // unverified
    [InlineData("email", "stranger@example.com", "true", false)]     // not listed
    [InlineData("email", "me@example.co", "true", false)]            // only part of a listed address
    public void Needs_a_verified_listed_email(string claimType, string email, string verified, bool allowed) =>
        Assert.Equal(allowed, List.IsAllowed(User(true, (claimType, email), ("email_verified", verified))));

    [Fact]
    public void Missing_email_or_verification_is_not_allowed()
    {
        Assert.False(List.IsAllowed(User(true, ("email", "me@example.com"))));
        Assert.False(List.IsAllowed(User(true, ("email_verified", "true"))));
    }

    [Fact]
    public void Not_signed_in_is_not_allowed() =>
        Assert.False(List.IsAllowed(User(false, ("email", "me@example.com"), ("email_verified", "true"))));
}
