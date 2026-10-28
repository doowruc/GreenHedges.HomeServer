using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Account;

/// <summary>
/// Signs out of the app and Auth0. POST only, so a link elsewhere can't sign you out. Anonymous so
/// that a signed-in user who isn't on the allow-list can still sign out and try another account.
/// </summary>
[AllowAnonymous]
public class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task OnPostAsync()
    {
        var properties = new LogoutAuthenticationPropertiesBuilder()
            .WithRedirectUri(Url.Page("/Account/SignedOut")!)
            .Build();
        await HttpContext.SignOutAsync(Auth0Constants.AuthenticationScheme, properties);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
