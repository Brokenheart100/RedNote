using Microsoft.EntityFrameworkCore;
using RedNote.Contracts.Users;
using RedNote.UserService.Infrastructure.Persistence;

namespace RedNote.UserService.Features.Users.GetMyFollowingIds;

public static class GetFollowingUserIdsHandler
{
    public static async Task<GetFollowingUserIdsResponse> Handle(
        GetFollowingUserIdsRequest request,
        UserServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new ArgumentException(
                "User id cannot be empty.",
                nameof(request));
        }

        var userIds = await dbContext.UserFollows
            .AsNoTracking()
            .Where(follow => follow.FollowerUserId == request.UserId)
            .OrderBy(follow => follow.CreatedAtUtc)
            .Select(follow => follow.FollowingUserId)
            .ToListAsync(cancellationToken);

        return new GetFollowingUserIdsResponse
        {
            UserIds = userIds
        };
    }
}