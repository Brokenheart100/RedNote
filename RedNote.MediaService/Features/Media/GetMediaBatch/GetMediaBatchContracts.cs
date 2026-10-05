namespace RedNote.MediaService.Features.Media.GetMediaBatch;

public sealed record GetMediaBatchRequest(
    IReadOnlyList<Guid>? MediaIds);
