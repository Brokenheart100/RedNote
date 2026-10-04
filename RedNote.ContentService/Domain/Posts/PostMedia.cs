namespace RedNote.ContentService.Domain.Posts;

public sealed class PostMedia
{
    private PostMedia()
    {
    }

    public PostMedia(
        Guid postId,
        Guid mediaId,
        int sortOrder)
    {
        if (postId == Guid.Empty)
        {
            throw new ArgumentException(
                "Post id cannot be empty.",
                nameof(postId));
        }

        if (mediaId == Guid.Empty)
        {
            throw new ArgumentException(
                "Media id cannot be empty.",
                nameof(mediaId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);

        PostId = postId;
        MediaId = mediaId;
        SortOrder = sortOrder;
    }

    public Guid PostId { get; private set; }

    public Guid MediaId { get; private set; }

    public int SortOrder { get; private set; }
}