namespace RedNote.ContentService.Features.Posts.Common;

public sealed record CreatePostRequest(
    string Title,
    string Content,
    IReadOnlyList<Guid>? MediaIds,
    IReadOnlyList<string>? Tags);

public sealed record PostMediaResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    string Url);

public sealed record PostAuthorResponse(
    Guid UserId,
    string? Nickname,
    string? AvatarUrl);

public sealed record PostResponse(
    Guid Id,
    Guid AuthorUserId,
    PostAuthorResponse Author,
    string Title,
    string Content,
    IReadOnlyList<Guid> MediaIds,
    IReadOnlyList<PostMediaResponse> Media,
    IReadOnlyList<string>? Tags,
    int LikeCount,
    int CommentCount,
    bool IsLiked,
    bool IsFavorited,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);