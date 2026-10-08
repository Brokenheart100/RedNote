using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.Contracts.Users;
using RedNote.UserService.Domain.Users;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.UserService.Features.Users.UpdateMe;

[ApiVersion("1.0")]
[Authorize]
public static class UpdateMeEndpoint
{
    [WolverinePatch("/users/me")]
    [Transactional]
    public static async Task<IResult> Patch(
        UpdateMeRequest request,
        ClaimsPrincipal principal,
        UserServiceDbContext dbContext,
        IMessageBus bus,
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
        await bus.PublishAsync(
            new UserProfileChanged(
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl,
                profile.UpdatedAtUtc));

        return Results.Ok(
            new UpdateMeResponse(
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl,
                profile.Bio,
                profile.CreatedAtUtc,
                profile.UpdatedAtUtc));
    }

}
