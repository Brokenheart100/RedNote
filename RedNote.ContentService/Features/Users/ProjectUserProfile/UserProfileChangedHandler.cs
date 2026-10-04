using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Users;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Users;
using Wolverine.Attributes;

namespace RedNote.ContentService.Features.Users.ProjectUserProfile;

[WolverineHandler]
public static class UserProfileChangedHandler
{
    public static async Task Handle(
        UserProfileChanged message,
        ContentServiceDbContext dbContext,
        ILogger<UserProfileChangedHandlerMarker> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "👤📨 [USER PROJECTION] 收到 UserProfileChanged | UserId={UserId} | Nickname={Nickname} | AvatarUrl={AvatarUrl} | UpdatedAtUtc={UpdatedAtUtc}",
            message.UserId,
            message.Nickname,
            message.AvatarUrl,
            message.UpdatedAtUtc);

        var projection = await dbContext.UserProfileProjections
            .SingleOrDefaultAsync(
                profile => profile.UserId == message.UserId,
                cancellationToken);

        if (projection is null)
        {
            logger.LogInformation(
                "🆕 [USER PROJECTION] 创建 Projection | UserId={UserId}",
                message.UserId);

            dbContext.UserProfileProjections.Add(
                new UserProfileProjection(
                    message.UserId,
                    message.Nickname,
                    message.AvatarUrl,
                    message.UpdatedAtUtc));

            await dbContext.SaveChangesAsync(
                cancellationToken);

            logger.LogInformation(
                "✅ [USER PROJECTION] Projection 创建成功 | UserId={UserId}",
                message.UserId);

            return;
        }

        if (message.UpdatedAtUtc <= projection.UpdatedAtUtc)
        {
            logger.LogInformation(
                "⏭️ [USER PROJECTION] 忽略旧事件 | UserId={UserId} | Event={EventUpdatedAtUtc} | Current={CurrentUpdatedAtUtc}",
                message.UserId,
                message.UpdatedAtUtc,
                projection.UpdatedAtUtc);

            return;
        }

        projection.Update(
            message.Nickname,
            message.AvatarUrl,
            message.UpdatedAtUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "✅ [USER PROJECTION] Projection 更新成功 | UserId={UserId} | Nickname={Nickname}",
            message.UserId,
            projection.Nickname);
    }
}

public sealed class UserProfileChangedHandlerMarker;