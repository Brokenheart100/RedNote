namespace RedNote.ContentService.Domain.Posts;

public sealed class PostLike
{
    private PostLike()
    {
    }

    public PostLike(
        Guid postId,
        Guid userId)
    {
        if (postId == Guid.Empty)
        {
            throw new ArgumentException(
                "Post id cannot be empty.",
                nameof(postId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User id cannot be empty.",
                nameof(userId));
        }

        PostId = postId;
        UserId = userId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid PostId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}