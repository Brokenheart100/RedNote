using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.ForgotPassword;

[ApiVersion("1.0")]
public static class ForgotPasswordEndpoint
{
    [WolverinePost(
        "/auth/forgot-password")]
    public static async Task<IResult> Post(
        ForgotPasswordRequest request,
        [FromServices] UserManager<ApplicationUser> userManager)
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

        var user =
            await userManager.FindByEmailAsync(
                request.Email);

        /*
         * 防止账号枚举。
         *
         * 用户不存在或者邮箱没有确认，
         * 都返回完全相同的 200。
         */
        if (
            user is null
            || !await userManager
                .IsEmailConfirmedAsync(
                    user)
        )
        {
            return Results.Ok();
        }

        var token =
            await userManager
                .GeneratePasswordResetTokenAsync(
                    user);

        var resetCode =
            WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    token));

        /*
         * 当前阶段先跑通流程。
         *
         * 后续接入 IEmailSender 后，
         * 应把 resetCode 发到邮箱，
         * 而不是直接返回给客户端。
         */
        return Results.Ok(
            new ForgotPasswordResponse(
                resetCode));
    }

    public sealed record ForgotPasswordRequest(
        string Email);

    public sealed record ForgotPasswordResponse(
        string ResetCode);
}