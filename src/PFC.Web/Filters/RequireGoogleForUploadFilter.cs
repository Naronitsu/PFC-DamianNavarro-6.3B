using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;

namespace PFC.Web.Filters;

public sealed class RequireGoogleForUploadFilter : IAsyncAuthorizationFilter
{
    private readonly IOptions<GcpOptions> _gcp;

    public RequireGoogleForUploadFilter(IOptions<GcpOptions> gcp)
    {
        _gcp = gcp;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!_gcp.Value.OAuthConfigured)
            return Task.CompletedTask;

        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
            return Task.CompletedTask;

        var returnUrl = $"{context.HttpContext.Request.Path}{context.HttpContext.Request.QueryString}";
        context.Result = new ChallengeResult(
            GoogleDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = returnUrl });

        return Task.CompletedTask;
    }
}
