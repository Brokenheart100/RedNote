using Asp.Versioning;
using RedNote.MediaService.Features.Media.Common;
using Wolverine.Http;

namespace RedNote.MediaService.Features.Media.GetMediaBatch;

[ApiVersion("1.0")]
public static class GetMediaBatchEndpoint
{
    [WolverinePost("/media/batch")]
    public static async Task<IResult> Post(
        GetMediaBatchRequest request,
        MediaQueryService queryService,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await queryService.GetBatchAsync(request.MediaIds, cancellationToken));
        }
        catch (MediaQueryValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mediaIds"] = [exception.ValidationMessage]
            });
        }
    }
}
