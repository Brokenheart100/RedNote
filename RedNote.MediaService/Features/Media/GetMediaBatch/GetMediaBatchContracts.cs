namespace RedNote.MediaService.Features.Media.GetMediaBatch;

public sealed record GetMediaBatchRequest(
    IReadOnlyList<Guid>? MediaIds);

public sealed record MediaBatchItem(
    Guid Id,
    Guid OwnerUserId,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset CreatedAtUtc,
    string Url);