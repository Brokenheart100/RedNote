namespace RedNote.Contracts.Content;

public sealed record PostMetricsChanged(
    Guid PostId,
    int LikeCount,
    int CommentCount,
    long Revision = 0);
