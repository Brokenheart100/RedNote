using Asp.Versioning;
using Microsoft.AspNetCore.Antiforgery;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.Csrf;

[ApiVersion("1.0")]
public static class CsrfEndpoint
{
    [WolverineGet("/auth/csrf")]
    public static IResult Get(
        HttpContext httpContext,
        IAntiforgery antiforgery)
    {
        var tokenSet =
            antiforgery.GetAndStoreTokens(
                httpContext);

        if (
            string.IsNullOrWhiteSpace(
                tokenSet.RequestToken)
        )
        {
            throw new InvalidOperationException(
                "Unable to generate antiforgery request token.");
        }

        if (
            string.IsNullOrWhiteSpace(
                tokenSet.HeaderName)
        )
        {
            throw new InvalidOperationException(
                "Antiforgery header name is unavailable.");
        }

        return Results.Ok(
            new CsrfResponse(
                tokenSet.RequestToken,
                tokenSet.HeaderName));
    }

    public sealed record CsrfResponse(
        string Token,
        string HeaderName);
}