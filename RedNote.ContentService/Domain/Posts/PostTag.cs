namespace RedNote.ContentService.Domain.Posts;

public sealed class PostTag
{
    private PostTag()
    {
    }

    public PostTag(
        Guid postId,
        string name)
    {
        if (postId == Guid.Empty)
        {
            throw new ArgumentException(
                "PostId cannot be empty.",
                nameof(postId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Tag name is required.",
                nameof(name));
        }

        var normalizedName =
            name.Trim();

        if (normalizedName.Length > 30)
        {
            throw new ArgumentException(
                "Tag name cannot exceed 30 characters.",
                nameof(name));
        }

        PostId = postId;
        Name = normalizedName;
    }

    public Guid PostId { get; private set; }

    public string Name { get; private set; } =
        string.Empty;
}