namespace RedNote.ContentService.Features.Posts.Update;

public sealed record UpdatePostRequest(
    string Title,
    string Content,
    IReadOnlyList<string>? Tags);