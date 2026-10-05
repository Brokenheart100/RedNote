using Asp.Versioning;
using RedNote.MediaService.Features.Media.Common;
using Wolverine.Http;

namespace RedNote.MediaService.Features.Media.GetMedia;

[ApiVersion("1.0")]
public static class GetMediaEndpoint
{
    [WolverineGet("/media/{mediaId:guid}")]
    public static async Task<IResult> Get(
        Guid mediaId,
        MediaQueryService queryService,
        CancellationToken cancellationToken)
    {
        if (mediaId == Guid.Empty) return Results.NotFound();
        var items = await queryService.GetBatchAsync([mediaId], cancellationToken);
        return items.Count == 0 ? Results.NotFound() : Results.Ok(items[0]);
    }
}
