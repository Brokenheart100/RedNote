namespace RedNote.UserService.Features.Users.GetUser;

public sealed record UserProfileResponse(
    Guid UserId,
    string? Nickname,
    string? AvatarUrl,
    string? Bio,
    int FollowersCount,
    int FollowingCount,
    bool IsFollowing,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);