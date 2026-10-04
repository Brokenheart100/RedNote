using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.UserService.Domain.Users;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.UserService.Features.Users.Follow;

[ApiVersion("1.0")]
[Authorize]
public static class FollowEndpoint
{
    [WolverinePost("/users/{userId:guid}/follow")]
    public static async Task<IResult> Follow(
        Guid userId,
        ClaimsPrincipal principal,
        UserServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(
                principal,
                out var currentUserId))
        {
            return Results.Unauthorized();
        }

        if (currentUserId == userId)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["userId"] =
                    [
                        "You cannot follow yourself."
                    ]
                });
        }

        var targetUserExists =
            await dbContext.UserProfiles
                .AsNoTracking()
                .AnyAsync(
                    profile =>
                        profile.UserId == userId,
                    cancellationToken);

        if (!targetUserExists)
        {
            return Results.NotFound();
        }

        var alreadyFollowing =
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

        if (alreadyFollowing)
        {
            return Results.NoContent();
        }

        var follow =
            new UserFollow(
                currentUserId,
                userId);

        dbContext.UserFollows.Add(
            follow);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return Results.NoContent();
    }

    [WolverineDelete("/api/v1/users/{userId:guid}/follow")]
    public static async Task<IResult> Unfollow(
        Guid userId,
        ClaimsPrincipal principal,
        UserServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(
                principal,
                out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var follow =
            await dbContext.UserFollows
                .SingleOrDefaultAsync(
                    follow =>
                        follow.FollowerUserId
                        == currentUserId
                        &&
                        follow.FollowingUserId
                        == userId,
                    cancellationToken);

        if (follow is null)
        {
            return Results.NoContent();
        }

        dbContext.UserFollows.Remove(
            follow);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return Results.NoContent();
    }

    private static bool TryGetCurrentUserId(
        ClaimsPrincipal principal,
        out Guid userId)
    {
        var subject =
            principal.FindFirst("sub")
                ?.Value;

        return Guid.TryParse(
            subject,
            out userId);
    }
}