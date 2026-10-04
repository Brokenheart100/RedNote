namespace RedNote.SearchService.Domain.Posts;

public sealed class PostSearchDocument
{
    public Guid Id { get; init; }

    public Guid AuthorUserId { get; init; }

    public string Title { get; init; }
        = string.Empty;

    public string Content { get; init; }
        = string.Empty;

    public IReadOnlyList<string> Tags { get; init; }
        = [];

    public int LikeCount { get; init; }

    public int CommentCount { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}