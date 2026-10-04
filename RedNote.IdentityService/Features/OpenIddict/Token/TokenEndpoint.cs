using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RedNote.IdentityService.Features.OpenIddict.Token;

public static class TokenEndpoint
{
    [WolverinePost("/connect/token")]
    public static async Task<IResult> Post(
        HttpContext context,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var request =
            context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OpenID Connect request cannot be retrieved.");

        if (
            !request.IsAuthorizationCodeGrantType()
            && !request.IsRefreshTokenGrantType()
        )
        {
            throw new InvalidOperationException(
                "The specified grant type is not supported.");
        }

        var result =
            await context.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults
                    .AuthenticationScheme);

        var sourcePrincipal =
            result.Principal;

        if (sourcePrincipal is null)
        {
            return InvalidGrant(
                "The token is no longer valid.");
        }

        var subject =
            sourcePrincipal.GetClaim(
                Claims.Subject);

        if (
            string.IsNullOrWhiteSpace(
                subject)
        )
        {
            return InvalidGrant(
                "The token is no longer valid.");
        }

        var user =
            await userManager.FindByIdAsync(
                subject);

        if (user is null)
        {
            return InvalidGrant(
                "The token is no longer valid.");
        }

        if (
            !await signInManager
                .CanSignInAsync(
                    user)
        )
        {
            return InvalidGrant(
                "The user is no longer allowed to sign in.");
        }

        /*
         * SecurityStamp 变化后，
         * 旧 refresh token 必须失效。
         *
         * 例如：
         * - 修改密码
         * - logout-everywhere
         */
        var securityStampUser =
            await signInManager
                .ValidateSecurityStampAsync(
                    sourcePrincipal);

        if (securityStampUser is null)
        {
            return InvalidGrant(
                "The token is no longer valid.");
        }

        var identity =
            new ClaimsIdentity(
                sourcePrincipal.Claims,
                authenticationType:
                    TokenValidationParameters
                        .DefaultAuthenticationType,
                nameType:
                    Claims.Name,
                roleType:
                    Claims.Role);

        /*
         * 使用数据库当前用户信息
         * 覆盖旧 token 中可能过时的 claims。
         */
        identity.SetClaim(
            Claims.Subject,
            user.Id.ToString());

        identity.SetClaim(
            Claims.Email,
            user.Email);

        identity.SetClaim(
            Claims.Name,
            user.DisplayName
            ?? user.Email);

        var roles =
            await userManager
                .GetRolesAsync(
                    user);

        identity.SetClaims(
            Claims.Role,
            [.. roles]);

        var principal =
            new ClaimsPrincipal(
                identity);

        /*
         * 延续 Authorization Code /
         * Refresh Token 中已经批准的 scopes/resources。
         */
        principal.SetScopes(
            sourcePrincipal.GetScopes());

        principal.SetResources(
            sourcePrincipal.GetResources());

        /*
         * SecurityStamp 可以参与内部 token 校验，
         * 但不能暴露到 access_token / id_token。
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
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults
                    .AuthenticationScheme);
    }

    private static IResult InvalidGrant(
        string description)
    {
        return Results.Forbid(
            new AuthenticationProperties(
                new Dictionary<
                    string,
                    string?
                >
                {
                    [
                        OpenIddictServerAspNetCoreConstants
                            .Properties.Error
                    ] =
                        Errors.InvalidGrant,

                    [
                        OpenIddictServerAspNetCoreConstants
                            .Properties.ErrorDescription
                    ] =
                        description
                }),
            [
                OpenIddictServerAspNetCoreDefaults
                    .AuthenticationScheme
            ]);
    }
}