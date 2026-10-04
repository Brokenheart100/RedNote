using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.Logout;

[ApiVersion("1.0")]
[Authorize]
public static class LogoutEndpoint
{
    [WolverinePost("/auth/logout")]
    public static async Task<IResult> Post(
        [FromServices] SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();

        return Results.Ok();
    }
}