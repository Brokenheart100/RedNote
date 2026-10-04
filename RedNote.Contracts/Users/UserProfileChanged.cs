namespace RedNote.Contracts.Users;

public sealed record UserProfileChanged(
    Guid UserId,
    string? Nickname,
    string? AvatarUrl,
    DateTimeOffset UpdatedAtUtc);