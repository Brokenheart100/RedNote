namespace RedNote.ContentService.Features.Posts.Common;

public sealed record CreatePostCommentRequest(
    string Content,
    Guid? ParentCommentId);

public sealed record CommentAuthorResponse(
    Guid UserId,
    string? Nickname,
    string? AvatarUrl);

public sealed record PostCommentResponse(
    Guid Id,
    Guid PostId,
    Guid AuthorUserId,
    CommentAuthorResponse Author,
    string Content,
    Guid? ParentCommentId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PostCommentItemResponse(
    Guid Id,
    Guid PostId,
    Guid AuthorUserId,
    CommentAuthorResponse Author,
    string Content,
    Guid? ParentCommentId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<PostCommentResponse> Replies);

public sealed record PostCommentsResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<PostCommentItemResponse> Items);