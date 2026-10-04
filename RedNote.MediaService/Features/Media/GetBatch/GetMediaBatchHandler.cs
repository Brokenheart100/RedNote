using Amazon.S3;
using Amazon.S3.Model;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using RedNote.Contracts.Media;
using RedNote.MediaService.Infrastructure.Persistence;

namespace RedNote.MediaService.Features.Media.GetBatch;

public static class GetMediaBatchHandler
{
    private const int MaxBatchSize =
        100;

    public static async Task<GetMediaBatchResponse> Handle(
        GetMediaBatchRequest request,
        IAmazonS3 s3,
        IConfiguration configuration,
        MediaServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var mediaIds =
            request.MediaIds
                .Distinct()
                .ToArray();

        if (mediaIds.Length == 0)
        {
            return new GetMediaBatchResponse();
        }

        if (mediaIds.Length > MaxBatchSize)
        {
            throw new ArgumentException(
                $"A maximum of {MaxBatchSize} media items is allowed.",
                nameof(request));
        }

        if (mediaIds.Any(
                mediaId =>
                    mediaId == Guid.Empty))
        {
            throw new ArgumentException(
                "Media id cannot be empty.",
                nameof(request));
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

        foreach (var media in mediaAssets)
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
                new MediaBatchItem
                {
                    Id =
                        media.Id,

                    OwnerUserId =
                        media.OwnerUserId,

                    FileName =
                        media.FileName,

                    ContentType =
                        media.ContentType,

                    Size =
                        media.Size,

                    CreatedAtUtc = media.CreatedAtUtc.UtcDateTime,

                    Url =
                        url
                });
        }

        return new GetMediaBatchResponse
        {
            Items =
                items
        };
    }
}