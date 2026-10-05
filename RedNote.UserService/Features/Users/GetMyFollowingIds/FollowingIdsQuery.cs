using Microsoft.EntityFrameworkCore;
using RedNote.UserService.Infrastructure.Persistence;

namespace RedNote.UserService.Features.Users.GetMyFollowingIds;

internal static class FollowingIdsQuery
{
    public static Task<List<Guid>> LoadAsync(
        UserServiceDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return dbContext.UserFollows
            .AsNoTracking()
            .Where(follow => follow.FollowerUserId == userId)
            .OrderBy(follow => follow.CreatedAtUtc)
            .Select(follow => follow.FollowingUserId)
            .ToListAsync(cancellationToken);
    }
}
