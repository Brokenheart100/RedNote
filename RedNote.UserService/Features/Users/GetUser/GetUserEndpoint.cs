using System.Security.Claims;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.UserService.Features.Users.GetUser;

[ApiVersion("1.0")]
public static class GetUserEndpoint
{
    [WolverineGet("/users/{userId:guid}")]
    public static async Task<IResult> Get(
        Guid userId,
        ClaimsPrincipal principal,
        UserServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var profile =
            await dbContext.UserProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    profile =>
                        profile.UserId == userId,
                    cancellationToken);

        if (profile is null)
        {
            return Results.NotFound();
        }

        var followersCount =
            await dbContext.UserFollows
                .AsNoTracking()
                .CountAsync(
                    follow =>
                        follow.FollowingUserId
                        == userId,
                    cancellationToken);

        var followingCount =
            await dbContext.UserFollows
                .AsNoTracking()
                .CountAsync(
                    follow =>
                        follow.FollowerUserId
                        == userId,
                    cancellationToken);

        var isFollowing =
            false;

        var subject =
            principal.FindFirst("sub")
                ?.Value;

        if (
            Guid.TryParse(
                subject,
                out var currentUserId)
            &&
            currentUserId != userId
        )
        {
            isFollowing =
                await dbContext.UserFollows
                    .AsNoTracking()
                    .AnyAsync(
                        follow =>
                            follow.FollowerUserId
                            == currentUserId
                            &&
                            follow.FollowingUserId
                            == userId,
                        cancellationToken);
        }

        return Results.Ok(
            new UserProfileResponse(
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl,
                profile.Bio,
                followersCount,
                followingCount,
                isFollowing,
                profile.CreatedAtUtc,
                profile.UpdatedAtUtc));
    }
}