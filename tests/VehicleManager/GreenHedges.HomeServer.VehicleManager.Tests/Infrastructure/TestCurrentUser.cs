using GreenHedges.HomeServer.Common.Web.Auth;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>Acts as a fixed user instead of reading the signed-in user from the request.</summary>
public class TestCurrentUser(string? id) : CurrentUser(new Microsoft.AspNetCore.Http.HttpContextAccessor())
{
    public override string? Id => id;
}
