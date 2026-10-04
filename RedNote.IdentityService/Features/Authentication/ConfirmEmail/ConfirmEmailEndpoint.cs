using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.ConfirmEmail;

[ApiVersion("1.0")]
public static class ConfirmEmailEndpoint
{
    [WolverinePost("/auth/confirm-email")]
    public static async Task<IResult> Post(
        ConfirmEmailRequest request,
        UserManager<ApplicationUser> userManager)
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
                request.Code)
        )
        {
            return ValidationProblem(
                "code",
                "Confirmation code is required.");
        }

        var user =
            await userManager.FindByEmailAsync(
                request.Email);

        if (user is null)
        {
            return InvalidCode();
        }

        string token;

        try
        {
            var bytes =
                WebEncoders.Base64UrlDecode(
                    request.Code);

            token =
                Encoding.UTF8.GetString(
                    bytes);
        }
        catch (FormatException)
        {
            return InvalidCode();
        }

        var result =
            await userManager
                .ConfirmEmailAsync(
                    user,
                    token);

        if (!result.Succeeded)
        {
            return InvalidCode();
        }

        return Results.Ok();
    }

    private static IResult InvalidCode()
    {
        return Results.ValidationProblem(
            new Dictionary<
                string,
                string[]
            >
            {
                ["code"] =
                [
                    "The confirmation code is invalid."
                ]
            });
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

    public sealed record ConfirmEmailRequest(
        string Email,
        string Code);
}