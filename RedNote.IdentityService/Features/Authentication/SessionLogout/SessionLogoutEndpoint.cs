using Asp.Versioning;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.SessionLogout;

[ApiVersion("1.0")]
public static class SessionLogoutEndpoint
{
    [WolverinePost("/auth/session/logout")]
    public static async Task<IResult> Post(HttpContext context,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] SignInManager<ApplicationUser> signInManager)
    {
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(); }
        await signInManager.SignOutAsync();
        return Results.Ok();
    }
}
