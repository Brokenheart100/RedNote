using RedNote.Contracts.Media;
using RedNote.MediaService.Features.Media.Common;

namespace RedNote.MediaService.Features.Media.GetBatch;

public static class GetMediaBatchHandler
{
    public static async Task<GetMediaBatchResponse> Handle(
        GetMediaBatchRequest request,
        MediaQueryService queryService,
        CancellationToken cancellationToken)
    {
        var items = await queryService.GetBatchAsync(request.MediaIds, cancellationToken);
        return new GetMediaBatchResponse
        {
            Items = items.Select(media => new MediaBatchItem
            {
                Id = media.Id,
                OwnerUserId = media.OwnerUserId,
                FileName = media.FileName,
                ContentType = media.ContentType,
                Size = media.Size,
                CreatedAtUtc = media.CreatedAtUtc.UtcDateTime,
                Url = media.Url
            }).ToList()
        };
    }
}
