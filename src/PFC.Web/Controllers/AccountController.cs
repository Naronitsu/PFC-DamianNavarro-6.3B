using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;

namespace PFC.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController : Controller
{
    private readonly GcpOptions _gcp;

    public AccountController(IOptions<GcpOptions> gcp)
    {
        _gcp = gcp.Value;
    }

    public IActionResult Login(string? returnUrl = null)
    {
        if (!_gcp.OAuthConfigured)
            return View("OAuthNotConfigured");

        var props = new AuthenticationProperties { RedirectUri = returnUrl ?? Url.Content("~/") };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return RedirectToAction("Index", "Home");
    }
}
