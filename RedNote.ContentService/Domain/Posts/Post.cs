namespace RedNote.ContentService.Domain.Posts;

public sealed class Post
{
    private Post()
    {
    }

    public Post(
        Guid id,
        Guid authorUserId,
        string title,
        string content)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Post id cannot be empty.",
                nameof(id));
        }

        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Author user id cannot be empty.",
                nameof(authorUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            title);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            content);

        Id = id;
        AuthorUserId = authorUserId;
        Title = title.Trim();
        Content = content.Trim();

        Status = PostStatus.Published;

        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Title { get; private set; } = null!;

    public string Content { get; private set; } = null!;

    public PostStatus Status { get; private set; }

    public long Revision { get; private set; } = 1;
    public bool IsHidden { get; private set; }

    public void SetHidden(bool hidden)
    {
        if (Status == PostStatus.Deleted) throw new InvalidOperationException("Deleted posts cannot be moderated.");
        if (IsHidden == hidden) return;
        IsHidden = hidden;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Revision++;
    }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string title,
        string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            title);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            content);

        Title = title.Trim();
        Content = content.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Revision++;
    }

    public void Delete()
    {
        Status = PostStatus.Deleted;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Revision++;
    }

    public void RecordMetricsChange() => Revision++;
    public void RecordInteractionChange() => Revision++;
}

public enum PostStatus
{
    Published = 1,
    Deleted = 2
}
