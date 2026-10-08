namespace RedNote.ContentService.Features.Posts.Common;

public sealed record CreatePostRequest(
    string Title,
    string Content,
    IReadOnlyList<Guid>? MediaIds,
    IReadOnlyList<string>? Tags);
