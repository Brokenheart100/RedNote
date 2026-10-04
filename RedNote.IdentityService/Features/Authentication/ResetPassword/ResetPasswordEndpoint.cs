using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.ResetPassword;

[ApiVersion("1.0")]
public static class ResetPasswordEndpoint
{
    [WolverinePost("/auth/reset-password")]
    public static async Task<IResult> Post(
        ResetPasswordRequest request,
        [FromServices] UserManager<ApplicationUser> userManager)
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
                request.ResetCode)
        )
        {
            return ValidationProblem(
                "resetCode",
                "Reset code is required.");
        }

        if (
            string.IsNullOrWhiteSpace(
                request.NewPassword)
        )
        {
            return ValidationProblem(
                "newPassword",
                "New password is required.");
        }

        var user =
            await userManager.FindByEmailAsync(
                request.Email);

        /*
         * 不暴露用户是否存在。
         */
        if (
            user is null
            || !await userManager
                .IsEmailConfirmedAsync(
                    user)
        )
        {
            return InvalidResetCode();
        }

        string resetToken;

        try
        {
            var decodedBytes =
                WebEncoders.Base64UrlDecode(
                    request.ResetCode);

            resetToken =
                Encoding.UTF8.GetString(
                    decodedBytes);
        }
        catch (FormatException)
        {
            return InvalidResetCode();
        }

        var result =
            await userManager
                .ResetPasswordAsync(
                    user,
                    resetToken,
                    request.NewPassword);

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

    private static IResult InvalidResetCode()
    {
        return Results.ValidationProblem(
            new Dictionary<
                string,
                string[]
            >
            {
                ["resetCode"] =
                    [
                        "The reset code is invalid."
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

    public sealed record ResetPasswordRequest(
        string Email,
        string ResetCode,
        string NewPassword);
}