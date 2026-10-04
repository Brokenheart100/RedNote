using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using RedNote.IdentityService.Domain.Users;
using Wolverine.Http;

namespace RedNote.IdentityService.Features.Authentication.ResendConfirmationEmail;

[ApiVersion("1.0")]
public static class ResendConfirmationEmailEndpoint
{
    [WolverinePost("/auth/resend-confirmation-email")]
    public static async Task<IResult> Post(
        ResendConfirmationEmailRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        IHostEnvironment environment)
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
         */
        if (user is null)
        {
            return Results.Ok();
        }

        /*
         * 已确认邮箱时不重复生成。
         */
        if (
            await userManager
                .IsEmailConfirmedAsync(
                    user)
        )
        {
            return Results.Ok();
        }

        var token =
            await userManager
                .GenerateEmailConfirmationTokenAsync(
                    user);

        var code =
            WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    token));

        /*
         * 当前开发阶段用于 E2E / Postman。
         *
         * 生产环境不能直接返回确认 token。
         */
        if (environment.IsDevelopment())
        {
            return Results.Ok(
                new ResendConfirmationEmailResponse(
                    code));
        }

        /*
         * 后续接 IEmailSender 后在这里发送邮件。
         */
        return Results.Ok();
    }

    public sealed record ResendConfirmationEmailRequest(
        string Email);

    public sealed record ResendConfirmationEmailResponse(
        string Code);
}