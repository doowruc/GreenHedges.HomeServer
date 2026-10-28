namespace GreenHedges.HomeServer.Common.Web.Data;

/// <summary>
/// Data that belongs to one signed-in user. The app's <see cref="AuditedDbContext"/> only returns the current user's
/// rows and stamps new rows with their id.
/// </summary>
public interface IUserOwned
{
    /// <summary>The Auth0 user id (the <c>sub</c> claim), e.g. <c>auth0|abc123</c>. Empty for unclaimed data.</summary>
    string UserId { get; set; }
}
