using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using RedNote.IdentityService.Infrastructure.OpenIddict;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.Admin;

[ApiVersion("1.0")]
public static class AdminLoginEndpoint
{
    [WolverinePost("/auth/admin/login")]
    [AllowAnonymous]
    [ValidateAntiforgery]
    public static async Task<IResult> Post(AdminLogin request,
        [FromServices] UserManager<ApplicationUser> users,
        [FromServices] SignInManager<ApplicationUser> signIn)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email);
        if (user is null || string.IsNullOrWhiteSpace(request.Password) || !await AdminIdentity.EligibleAsync(user, users))
            return Results.Unauthorized();
        var passwordResult = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!passwordResult.Succeeded) return Results.Unauthorized();
        var valid = !string.IsNullOrWhiteSpace(request.Code) && await users.VerifyTwoFactorTokenAsync(user,
            users.Options.Tokens.AuthenticatorTokenProvider, request.Code.Replace(" ", "", StringComparison.Ordinal));
        if (!valid) { await users.AccessFailedAsync(user); return Results.Unauthorized(); }
        await users.ResetAccessFailedCountAsync(user);
        await signIn.SignInWithClaimsAsync(user, false, [new Claim(AdminIdentity.MfaTime,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))]);
        return Results.Ok();
    }
}

public sealed record AdminLogin(string Email, string Password, string Code);
