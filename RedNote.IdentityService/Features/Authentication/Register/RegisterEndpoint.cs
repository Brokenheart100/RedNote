using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.Register;

[ApiVersion("1.0")]
public static class RegisterEndpoint
{
    [WolverinePost("/auth/register")]
    public static async Task<IResult> Post(
        RegisterRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (
            string.IsNullOrWhiteSpace(
                request.Email)
        )
        {
            return Results.ValidationProblem(
                new Dictionary<
                    string,
                    string[]
                >
                {
                    ["email"] =
                    [
                        "Email is required."
                    ]
                });
        }

        if (
            string.IsNullOrWhiteSpace(
                request.Password)
        )
        {
            return Results.ValidationProblem(
                new Dictionary<
                    string,
                    string[]
                >
                {
                    ["password"] =
                    [
                        "Password is required."
                    ]
                });
        }

        var user =
            new ApplicationUser
            {
                Id =
                    Guid.NewGuid(),

                UserName =
                    request.Email,

                Email =
                    request.Email,

                DisplayName =
                    request.DisplayName,

                FamilyName =
                    request.FamilyName
            };

        var result =
            await userManager.CreateAsync(
                user,
                request.Password);

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

        return Results.Ok(
            new RegisterResponse(
                user.Id,
                user.Email!,
                user.DisplayName,
                user.FamilyName,
                user.CreatedAtUtc));
    }
}