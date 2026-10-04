namespace RedNote.ContentService.Domain.Users;

public sealed class UserProfileProjection
{
    private UserProfileProjection()
    {
    }

    public UserProfileProjection(
        Guid userId,
        string? nickname,
        string? avatarUrl,
        DateTimeOffset updatedAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID cannot be empty.",
                nameof(userId));
        }

        UserId = userId;
        Nickname = Normalize(nickname, 64);
        AvatarUrl = Normalize(avatarUrl, 2_048);
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid UserId { get; private set; }

    public string? Nickname { get; private set; }

    public string? AvatarUrl { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string? nickname,
        string? avatarUrl,
        DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc <= UpdatedAtUtc)
        {
            return;
        }

        Nickname = Normalize(nickname, 64);
        AvatarUrl = Normalize(avatarUrl, 2_048);
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string? Normalize(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}