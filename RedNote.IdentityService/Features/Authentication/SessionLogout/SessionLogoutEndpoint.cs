using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.SessionLogout;

[ApiVersion("1.0")]
public static class SessionLogoutEndpoint
{
    [WolverinePost("/auth/session/logout")]
    [ValidateAntiforgery]
    public static async Task<IResult> Post(
        [FromServices] SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Ok();
    }
}
