using Microsoft.AspNetCore.Http;
using GreenHedges.HomeServer.Common.Web.Auth;

namespace GreenHedges.HomeServer.Common.Web.Tests;

public class CurrentUserTests
{
    [Fact]
    public void Background_work_can_act_as_a_user()
    {
        var user = new CurrentUser(new HttpContextAccessor());
        Assert.Null(user.Id);
        user.ActAs("auth0|someone");
        Assert.Equal("auth0|someone", user.Id);
    }

    [Fact]
    public void A_request_cant_act_as_another_user()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        Assert.Throws<InvalidOperationException>(() => new CurrentUser(accessor).ActAs("auth0|someone"));
    }
}
