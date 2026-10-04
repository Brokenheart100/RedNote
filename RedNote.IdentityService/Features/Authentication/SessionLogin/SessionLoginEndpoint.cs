using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.SessionLogin;

[ApiVersion("1.0")]
public static class SessionLoginEndpoint
{
    [WolverinePost("/auth/session/login")]
    public static async Task<IResult> Post(
        SessionLoginRequest request,
        [FromServices] SignInManager<ApplicationUser> signInManager)
    {
        if (
            string.IsNullOrWhiteSpace(
                request.Email)
        )
        {
            return ValidationProblem(
                "email",
                "Email is required.");
        }

        if (
            string.IsNullOrWhiteSpace(
                request.Password)
        )
        {
            return ValidationProblem(
                "password",
                "Password is required.");
        }

        signInManager.AuthenticationScheme =
            IdentityConstants.ApplicationScheme;

        var result =
            await signInManager.PasswordSignInAsync(
                request.Email,
                request.Password,
                isPersistent: false,
                lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return Results.Problem(
                title:
                    "Account locked.",

                detail:
                    "The account is temporarily locked.",

                statusCode:
                    StatusCodes.Status423Locked);
        }

        if (!result.Succeeded)
        {
            return Results.Problem(
                title:
                    "Login failed.",

                detail:
                    "Invalid email or password.",

                statusCode:
                    StatusCodes.Status401Unauthorized);
        }

        return Results.Ok();
    }

    private static IResult ValidationProblem(
        string key,
        string message)
    {
        return Results.ValidationProblem(
            new Dictionary<
                string,
                string[]
            >
            {
                [key] =
                [
                    message
                ]
            });
    }

    public sealed record SessionLoginRequest(
        string Email,
        string Password);
}