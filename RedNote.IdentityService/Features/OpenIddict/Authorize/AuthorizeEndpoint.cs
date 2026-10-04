using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RedNote.IdentityService.Features.OpenIddict.Authorize;

public static class AuthorizeEndpoint
{
    [WolverineGet("/connect/authorize")]
    [WolverinePost("/connect/authorize")]
    public static async Task<IResult> Handle(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] IConfiguration configuration)
    {
        var request =
            context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OpenID Connect request cannot be retrieved.");

        var result =
            await context.AuthenticateAsync(
                IdentityConstants.ApplicationScheme);

        /*
         * 当前用户尚未登录。
         *
         * 跳转到 Nuxt 登录页，并把当前 OIDC authorize 请求
         * 作为 returnUrl 传给前端。
         */
        if (
            !result.Succeeded
            || result.Principal is null
        )
        {
            var publicBaseUrl =
                GetRequiredAbsoluteUri(
                    configuration,
                    "OpenIddict:PublicBaseUrl");

            var loginUri =
                GetRequiredAbsoluteUri(
                    configuration,
                    "OpenIddict:Clients:RedNoteWeb:LoginUri");

            var authorizationUrl =
                BuildAuthorizationUrl(
                    publicBaseUrl,
                    context.Request);

            var loginUrl =
                BuildLoginUrl(
                    loginUri,
                    authorizationUrl);

            return Results.Redirect(
                loginUrl);
        }

        var user =
            await userManager.GetUserAsync(
                result.Principal);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        /*
         * 使用 ASP.NET Core Identity 创建完整 Principal。
         *
         * SecurityStamp 等 Identity 内部 Claims
         * 会被正确保留下来。
         */
        var principal =
            await signInManager
                .CreateUserPrincipalAsync(
                    user);

        /*
         * 标准 OIDC claims。
         */
        principal.SetClaim(
            Claims.Subject,
            user.Id.ToString());

        principal.SetClaim(
            Claims.Email,
            user.Email);

        principal.SetClaim(
            Claims.Name,
            user.DisplayName
            ?? user.Email);

        /*
         * 只授予本次 Authorization Request
         * 实际申请的 scopes。
         */
        principal.SetScopes(
            request.GetScopes());

        principal.SetResources(
            "rednote-api");

        /*
         * SecurityStamp 可以存在于 authorization code /
         * refresh token 内部，但不能暴露到 access token
         * 或 id_token。
         */
        var securityStampClaimType =
            userManager.Options
                .ClaimsIdentity
                .SecurityStampClaimType;

        principal.SetDestinations(
            claim =>
                claim.Type switch
                {
                    Claims.Subject =>
                    [
                        Destinations.AccessToken,
                        Destinations.IdentityToken
                    ],

                    Claims.Email
                        when principal.HasScope(
                            Scopes.Email) =>
                    [
                        Destinations.AccessToken,
                        Destinations.IdentityToken
                    ],

                    Claims.Name
                        when principal.HasScope(
                            Scopes.Profile) =>
                    [
                        Destinations.AccessToken,
                        Destinations.IdentityToken
                    ],

                    Claims.Role =>
                    [
                        Destinations.AccessToken
                    ],

                    _ when claim.Type
                        == securityStampClaimType =>
                    [],

                    _ =>
                    [
                        Destinations.AccessToken
                    ]
                });

        return Results.SignIn(
            principal,
            properties: null,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults
                    .AuthenticationScheme);
    }

    private static string BuildAuthorizationUrl(
        Uri publicBaseUrl,
        HttpRequest request)
    {
        var baseUrl =
            publicBaseUrl
                .ToString()
                .TrimEnd('/');

        return
            baseUrl
            + request.PathBase
            + request.Path
            + request.QueryString;
    }

    private static string BuildLoginUrl(
        Uri loginUri,
        string authorizationUrl)
    {
        var separator =
            string.IsNullOrEmpty(
                loginUri.Query)
                ? "?"
                : "&";

        return
            loginUri
            + separator
            + "returnUrl="
            + Uri.EscapeDataString(
                authorizationUrl);
    }

    private static Uri GetRequiredAbsoluteUri(
        IConfiguration configuration,
        string configurationKey)
    {
        var value =
            configuration[
                configurationKey];

        if (
            string.IsNullOrWhiteSpace(
                value)
        )
        {
            throw new InvalidOperationException(
                $"Configuration '{configurationKey}' is required.");
        }

        if (
            !Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri)
        )
        {
            throw new InvalidOperationException(
                $"Configuration '{configurationKey}' "
                + "must be an absolute URI. "
                + $"Value: '{value}'.");
        }

        return uri;
    }
}