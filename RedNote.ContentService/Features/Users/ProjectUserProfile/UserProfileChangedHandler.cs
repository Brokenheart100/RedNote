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
        CancellationToken cancellationToken)
    {
        var projection = await dbContext.UserProfileProjections
            .SingleOrDefaultAsync(profile => profile.UserId == message.UserId, cancellationToken);

        if (projection is null)
        {
            dbContext.UserProfileProjections.Add(
                new UserProfileProjection(
                    message.UserId,
                    message.Nickname,
                    message.AvatarUrl,
                    message.UpdatedAtUtc));

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (message.UpdatedAtUtc <= projection.UpdatedAtUtc)
        {
            return;
        }

        projection.Update(
            message.Nickname,
            message.AvatarUrl,
            message.UpdatedAtUtc);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class UserProfileChangedHandlerMarker;