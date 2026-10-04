using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.Contracts.Users;
using RedNote.UserService.Domain.Users;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace RedNote.UserService.Features.Users.UpdateMe;

[ApiVersion("1.0")]
[Authorize]
public static class UpdateMeEndpoint
{
    [WolverinePatch("/users/me")]
    public static async Task<IResult> Patch(
        UpdateMeRequest request,
        ClaimsPrincipal principal,
        IDbContextOutbox<UserServiceDbContext> outbox,
        CancellationToken cancellationToken)
    {
        var subject =
            principal.FindFirst("sub")?.Value;

        if (
            string.IsNullOrWhiteSpace(subject)
            ||
            !Guid.TryParse(
                subject,
                out var userId)
        )
        {
            return Results.Unauthorized();
        }

        if (
            request.Nickname
            is { Length: > 64 }
        )
        {
            return ValidationProblem(
                "nickname",
                "Nickname cannot exceed 64 characters.");
        }

        if (
            request.AvatarUrl
            is { Length: > 2048 }
        )
        {
            return ValidationProblem(
                "avatarUrl",
                "Avatar URL cannot exceed 2048 characters.");
        }

        if (
            request.Bio
            is { Length: > 500 }
        )
        {
            return ValidationProblem(
                "bio",
                "Bio cannot exceed 500 characters.");
        }

        var dbContext =
            outbox.DbContext;

        var profile =
            await dbContext.UserProfiles
                .SingleOrDefaultAsync(
                    profile =>
                        profile.UserId
                        == userId,
                    cancellationToken);

        if (profile is null)
        {
            profile =
                new UserProfile(
                    userId);

            dbContext.UserProfiles.Add(
                profile);
        }

        profile.Update(
            request.Nickname,
            request.AvatarUrl,
            request.Bio);

        /*
         * Integration Event
         *
         * 注意：
         * 使用经过 Domain Entity 规范化后的最终值，
         * 而不是直接使用 request。
         */
        await outbox.PublishAsync(
            new UserProfileChanged(
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl,
                profile.UpdatedAtUtc));

        /*
         * 一次性提交：
         *
         * - UserProfiles
         * - Wolverine Outbox message
         *
         * 避免 DB + RabbitMQ 双写问题。
         */
        await outbox.SaveChangesAndFlushMessagesAsync(
            cancellationToken);

        return Results.Ok(
            new UpdateMeResponse(
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl,
                profile.Bio,
                profile.CreatedAtUtc,
                profile.UpdatedAtUtc));
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
}