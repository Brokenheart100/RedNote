namespace RedNote.UserService.Domain.Users;

public sealed class UserProfile
{
    private UserProfile()
    {
    }

    public UserProfile(Guid userId)
    {
        UserId = userId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid UserId { get; private set; }

    public string? Nickname { get; private set; }

    public string? AvatarUrl { get; private set; }

    public string? Bio { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string? nickname,
        string? avatarUrl,
        string? bio)
    {
        Nickname = Normalize(nickname);
        AvatarUrl = Normalize(avatarUrl);
        Bio = Normalize(bio);

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string? Normalize(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}