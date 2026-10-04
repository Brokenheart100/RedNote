using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.UserService.Features.Users.FollowList;

[ApiVersion("1.0")]
public static class UserFollowListEndpoint
{
    [WolverineGet("/users/{userId:guid}/followers")]
    public static async Task<IResult> GetFollowers(
        Guid userId,
        int page,
        int pageSize,
        UserServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var pagingResult =
            ValidatePaging(
                page,
                pageSize);

        if (pagingResult is not null)
        {
            return pagingResult;
        }

        var userExists =
            await dbContext.UserProfiles
                .AsNoTracking()
                .AnyAsync(
                    profile =>
                        profile.UserId == userId,
                    cancellationToken);

        if (!userExists)
        {
            return Results.NotFound();
        }

        var query =
            from follow
                in dbContext.UserFollows
                    .AsNoTracking()

            join profile
                in dbContext.UserProfiles
                    .AsNoTracking()

                on follow.FollowerUserId
                equals profile.UserId

            where
                follow.FollowingUserId
                == userId

            orderby
                follow.CreatedAtUtc
                descending

            select new UserFollowListItem(
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl,
                profile.Bio);

        var totalCount =
            await query.CountAsync(
                cancellationToken);

        var items =
            await query
                .Skip(
                    (page - 1)
                    * pageSize)
                .Take(
                    pageSize)
                .ToListAsync(
                    cancellationToken);

        return Results.Ok(
            new UserFollowListResponse(
                page,
                pageSize,
                totalCount,
                items));
    }

    [WolverineGet(
        "/api/v1/users/{userId:guid}/following")]
    public static async Task<IResult> GetFollowing(
        Guid userId,
        int page,
        int pageSize,
        UserServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var pagingResult =
            ValidatePaging(
                page,
                pageSize);

        if (pagingResult is not null)
        {
            return pagingResult;
        }

        var userExists =
            await dbContext.UserProfiles
                .AsNoTracking()
                .AnyAsync(
                    profile =>
                        profile.UserId == userId,
                    cancellationToken);

        if (!userExists)
        {
            return Results.NotFound();
        }

        var query =
            from follow
                in dbContext.UserFollows
                    .AsNoTracking()

            join profile
                in dbContext.UserProfiles
                    .AsNoTracking()

                on follow.FollowingUserId
                equals profile.UserId

            where
                follow.FollowerUserId
                == userId

            orderby
                follow.CreatedAtUtc
                descending

            select new UserFollowListItem(
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl,
                profile.Bio);

        var totalCount =
            await query.CountAsync(
                cancellationToken);

        var items =
            await query
                .Skip(
                    (page - 1)
                    * pageSize)
                .Take(
                    pageSize)
                .ToListAsync(
                    cancellationToken);

        return Results.Ok(
            new UserFollowListResponse(
                page,
                pageSize,
                totalCount,
                items));
    }

    private static IResult? ValidatePaging(
        int page,
        int pageSize)
    {
        if (page < 1)
        {
            return Results.ValidationProblem(
                new Dictionary<
                    string,
                    string[]
                >
                {
                    ["page"] =
                    [
                        "Page must be greater "
                        + "than or equal to 1."
                    ]
                });
        }

        if (pageSize is < 1 or > 100)
        {
            return Results.ValidationProblem(
                new Dictionary<
                    string,
                    string[]
                >
                {
                    ["pageSize"] =
                    [
                        "PageSize must be "
                        + "between 1 and 100."
                    ]
                });
        }

        return null;
    }
}