using Microsoft.EntityFrameworkCore;
using RedNote.Contracts.Users;
using RedNote.UserService.Infrastructure.Persistence;

namespace RedNote.UserService.Features.Users.GetUsersBatch;

public static class GetUsersBatchHandler
{
    private const int MaxUserCount = 100;

    public static async Task<GetUsersBatchResponse> Handle(GetUsersBatchRequest request, UserServiceDbContext dbContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userIds = request.UserIds.Where(userId => userId != Guid.Empty).Distinct().ToArray();

        if (userIds.Length == 0)
            return new GetUsersBatchResponse();

        if (userIds.Length > MaxUserCount)
            throw new ArgumentException($"A maximum of {MaxUserCount} users can be queried at once.", nameof(request));

        var users = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .Select(profile => new UserSummary
            {
                UserId = profile.UserId,
                Nickname = profile.Nickname,
                AvatarUrl = profile.AvatarUrl
            })
            .ToListAsync(cancellationToken);

        return new GetUsersBatchResponse { Users = users };
    }
}