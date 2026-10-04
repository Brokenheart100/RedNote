using Amazon.S3;
using Amazon.S3.Model;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using RedNote.MediaService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.MediaService.Features.Media.GetMediaBatch;

[ApiVersion("1.0")]
public static class GetMediaBatchEndpoint
{
    private const int MaxBatchSize = 100;

    [WolverinePost("/media/batch")]
    public static async Task<IResult> Post(
        GetMediaBatchRequest request,
        IAmazonS3 s3,
        IConfiguration configuration,
        MediaServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var mediaIds =
            request.MediaIds?
                .Distinct()
                .ToArray()
            ?? [];

        if (mediaIds.Length == 0)
        {
            return Results.Ok(
                Array.Empty<MediaBatchItem>());
        }

        if (
            mediaIds.Length
            > MaxBatchSize
        )
        {
            return Results.ValidationProblem(
                new Dictionary<
                    string,
                    string[]
                >
                {
                    ["mediaIds"] =
                    [
                        $"A maximum of {MaxBatchSize} media items is allowed."
                    ]
                });
        }

        if (
            mediaIds.Any(
                mediaId =>
                    mediaId
                    == Guid.Empty)
        )
        {
            return Results.ValidationProblem(
                new Dictionary<
                    string,
                    string[]
                >
                {
                    ["mediaIds"] =
                    [
                        "Media id cannot be empty."
                    ]
                });
        }

        var bucketName =
            configuration["S3:BucketName"]
            ?? throw new InvalidOperationException(
                "S3 bucket name was not found.");

        var mediaAssets =
            await dbContext.MediaAssets
                .AsNoTracking()
                .Where(
                    media =>
                        mediaIds.Contains(
                            media.Id))
                .Select(
                    media => new
                    {
                        media.Id,
                        media.OwnerUserId,
                        media.FileName,
                        media.ContentType,
                        media.Size,
                        media.ObjectKey,
                        media.CreatedAtUtc
                    })
                .ToListAsync(
                    cancellationToken);

        var items =
            new List<MediaBatchItem>(
                mediaAssets.Count);

        foreach (
            var media
            in mediaAssets
        )
        {
            var url =
                await s3.GetPreSignedURLAsync(
                    new GetPreSignedUrlRequest
                    {
                        BucketName =
                            bucketName,

                        Key =
                            media.ObjectKey,

                        Verb =
                            HttpVerb.GET,

                        Expires =
                            DateTime.UtcNow
                                .AddMinutes(15),

                        Protocol =
                            Protocol.HTTP
                    });

            items.Add(
                new MediaBatchItem(
                    media.Id,
                    media.OwnerUserId,
                    media.FileName,
                    media.ContentType,
                    media.Size,
                    media.CreatedAtUtc,
                    url));
        }

        return Results.Ok(
            items);
    }
}