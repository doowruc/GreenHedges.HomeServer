using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Account;

/// <summary>Where unauthenticated requests are sent; hands over to Auth0's sign-in page.</summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    public IActionResult OnGet(string? returnUrl)
    {
        // Only return to pages in this app, so the link can't be used to redirect elsewhere.
        var properties = new LoginAuthenticationPropertiesBuilder()
            .WithRedirectUri(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Page("/Index")!)
            .Build();
        return Challenge(properties, Auth0Constants.AuthenticationScheme);
    }
}
