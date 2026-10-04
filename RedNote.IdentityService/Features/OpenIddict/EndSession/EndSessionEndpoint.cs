using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.OpenIddict.EndSession;

public static class EndSessionEndpoint
{
    [WolverineGet("/connect/logout")]
    [WolverinePost("/connect/logout")]
    public static async Task<IResult> Handle(
        SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();

        return Results.SignOut(
            new AuthenticationProperties
            {
                RedirectUri =
                    "/"
            },
            [
                OpenIddictServerAspNetCoreDefaults
                    .AuthenticationScheme
            ]);
    }
}