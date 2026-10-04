namespace RedNote.Contracts.Content;

public sealed record PostPublished(
    Guid PostId,
    Guid AuthorUserId,
    string Title,
    string Content,
    IReadOnlyList<string> Tags,
    int LikeCount,
    int CommentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);