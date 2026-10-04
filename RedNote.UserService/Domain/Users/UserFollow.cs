namespace RedNote.UserService.Domain.Users;

public sealed class UserFollow
{
    private UserFollow()
    {
    }

    public UserFollow(
        Guid followerUserId,
        Guid followingUserId)
    {
        if (followerUserId == followingUserId)
        {
            throw new ArgumentException(
                "A user cannot follow themselves.");
        }

        FollowerUserId = followerUserId;
        FollowingUserId = followingUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid FollowerUserId { get; private set; }

    public Guid FollowingUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}