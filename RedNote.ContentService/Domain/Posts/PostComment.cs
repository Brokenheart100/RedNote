namespace RedNote.ContentService.Domain.Posts;

public sealed class PostComment
{
    private PostComment()
    {
    }

    public PostComment(
        Guid id,
        Guid postId,
        Guid authorUserId,
        string content,
        Guid? parentCommentId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Comment id cannot be empty.",
                nameof(id));
        }

        if (postId == Guid.Empty)
        {
            throw new ArgumentException(
                "Post id cannot be empty.",
                nameof(postId));
        }

        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Author user id cannot be empty.",
                nameof(authorUserId));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException(
                "Comment content is required.",
                nameof(content));
        }

        if (content.Length > 1000)
        {
            throw new ArgumentException(
                "Comment content cannot exceed 1000 characters.",
                nameof(content));
        }

        if (parentCommentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Parent comment id cannot be empty.",
                nameof(parentCommentId));
        }

        Id = id;
        PostId = postId;
        AuthorUserId = authorUserId;
        Content = content;
        ParentCommentId = parentCommentId;
        Status = PostCommentStatus.Published;

        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid PostId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public Guid? ParentCommentId { get; private set; }

    public PostCommentStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException(
                "Comment content is required.",
                nameof(content));
        }

        if (content.Length > 1000)
        {
            throw new ArgumentException(
                "Comment content cannot exceed 1000 characters.",
                nameof(content));
        }

        Content = content;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Delete()
    {
        if (Status == PostCommentStatus.Deleted)
        {
            return;
        }

        Status = PostCommentStatus.Deleted;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

public enum PostCommentStatus
{
    Published = 1,
    Deleted = 2
}