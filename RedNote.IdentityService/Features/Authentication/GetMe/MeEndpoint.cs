using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.GetMe;

[ApiVersion("1.0")]
[Authorize]
public static class GetMeEndpoint
{
    [WolverineGet("/auth/me")]
    public static async Task<IResult> Get(
        ClaimsPrincipal principal,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user =
            await userManager.GetUserAsync(
                principal);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(
            new GetMeResponse(
                user.Id,
                user.Email,
                user.DisplayName,
                user.FamilyName,
                user.EmailConfirmed,
                user.CreatedAtUtc));
    }

    private sealed record GetMeResponse(
        Guid Id,
        string? Email,
        string? DisplayName,
        string? FamilyName,
        bool EmailConfirmed,
        DateTimeOffset CreatedAtUtc);
}