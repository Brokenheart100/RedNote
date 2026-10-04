using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.Contracts.Users;
using RedNote.UserService.Domain.Users;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace RedNote.UserService.Features.Users.GetMe;

[ApiVersion("1.0")]
[Authorize]
public static class GetMeEndpoint
{
    [WolverineGet("/users/me")]
    public static async Task<GetMeResponse> Get(
        ClaimsPrincipal user,
        IDbContextOutbox<UserServiceDbContext> outbox,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("GetMeEndpoint");

        var subject = user.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new UnauthorizedAccessException(
                "The access token does not contain a subject claim.");
        }

        if (!Guid.TryParse(subject, out var userId))
        {
            throw new UnauthorizedAccessException(
                "The subject claim is not a valid user identifier.");
        }

        var tokenDisplayName = Normalize(
            user.FindFirstValue("name"));

        logger.LogInformation(
            "🔐 [GET ME] Token claims | Sub={Sub} | Name={Name}",
            subject,
            tokenDisplayName);

        var dbContext = outbox.DbContext;

        var profile =
            await dbContext.UserProfiles
                .SingleOrDefaultAsync(
                    profile => profile.UserId == userId,
                    cancellationToken);

        logger.LogInformation(
            "👤 [GET ME] Profile lookup | UserId={UserId} | Exists={Exists} | Nickname={Nickname} | AvatarUrl={AvatarUrl}",
            userId,
            profile is not null,
            profile?.Nickname,
            profile?.AvatarUrl);

        var profileChanged = false;

        if (profile is null)
        {
            profile = new UserProfile(userId);

            if (tokenDisplayName is not null)
            {
                profile.Update(
                    nickname: tokenDisplayName,
                    avatarUrl: null,
                    bio: null);
            }

            dbContext.UserProfiles.Add(profile);

            profileChanged = true;
        }
        else if (
            string.IsNullOrWhiteSpace(profile.Nickname)
            && tokenDisplayName is not null)
        {
            profile.Update(
                nickname: tokenDisplayName,
                avatarUrl: profile.AvatarUrl,
                bio: profile.Bio);

            profileChanged = true;
        }

        if (profileChanged)
        {
            logger.LogInformation(
                "📤 [GET ME] 准备发布 UserProfileChanged | UserId={UserId} | Nickname={Nickname} | AvatarUrl={AvatarUrl}",
                profile.UserId,
                profile.Nickname,
                profile.AvatarUrl);

            await outbox.PublishAsync(
                new UserProfileChanged(
                    profile.UserId,
                    profile.Nickname,
                    profile.AvatarUrl,
                    profile.UpdatedAtUtc));

            logger.LogInformation(
                "📦 [GET ME] UserProfileChanged 已加入 Outbox | UserId={UserId}",
                profile.UserId);

            await outbox.SaveChangesAndFlushMessagesAsync(
                cancellationToken);

            logger.LogInformation(
                "✅ [GET ME] Outbox flush 完成 | UserId={UserId}",
                profile.UserId);
        }

        var followingCount =
            await dbContext.UserFollows
                .AsNoTracking()
                .CountAsync(
                    follow =>
                        follow.FollowerUserId == userId,
                    cancellationToken);

        var followersCount =
            await dbContext.UserFollows
                .AsNoTracking()
                .CountAsync(
                    follow =>
                        follow.FollowingUserId == userId,
                    cancellationToken);

        return new GetMeResponse(
            profile.UserId,
            profile.Nickname,
            profile.AvatarUrl,
            profile.Bio,
            followersCount,
            followingCount,
            false,
            profile.CreatedAtUtc,
            profile.UpdatedAtUtc);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

public sealed record GetMeResponse(
    Guid UserId,
    string? Nickname,
    string? AvatarUrl,
    string? Bio,
    int FollowersCount,
    int FollowingCount,
    bool IsFollowing,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);