using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.LogoutEverywhere;


[ApiVersion("1.0")]
[Authorize]
public static class LogoutEverywhereEndpoint
{
    [WolverinePost("/auth/logout-everywhere")]
    public static async Task<IResult> Post(
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

        var result =
            await userManager
                .UpdateSecurityStampAsync(
                    user);

        if (!result.Succeeded)
        {
            var errors =
                result.Errors
                    .GroupBy(
                        error =>
                            error.Code)
                    .ToDictionary(
                        group =>
                            group.Key,

                        group =>
                            group
                                .Select(
                                    error =>
                                        error.Description)
                                .ToArray());

            return Results.ValidationProblem(
                errors);
        }

        return Results.Ok();
    }
}