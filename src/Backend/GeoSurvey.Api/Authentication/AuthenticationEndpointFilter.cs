using System.Security.Cryptography;
using GeoSurvey.Identity.Application.Authentication;
using Microsoft.AspNetCore.Antiforgery;

namespace GeoSurvey.Api.Authentication;

/// <summary>Auth proof and cookie operations use an anonymous antiforgery token, independent of bearer identity.</summary>
public sealed class AuthenticationEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        try
        {
            var principal = context.HttpContext.User;
            try
            {
                context.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
                await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
            }
            finally { context.HttpContext.User = principal; }
            return await next(context);
        }
        catch (AntiforgeryValidationException)
        { return Results.Problem(statusCode: 400, title: "The request could not be verified. Refresh the page and try again."); }
        catch (AuthenticationSessionChangedException)
        { return Results.Problem(statusCode: 409, title: "The browser session changed. Sign in again to continue.", extensions: new Dictionary<string, object?> { ["code"] = "session_changed" }); }
        catch (Exception exception) when (exception is InvalidOperationException or CryptographicException)
        { return Results.Problem(statusCode: 503, title: "Authentication is temporarily unavailable. Try again shortly."); }
    }
}
