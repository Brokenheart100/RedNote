using Amazon.S3;
using Amazon.S3.Model;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using RedNote.MediaService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.MediaService.Features.Media.GetMedia;

[ApiVersion("1.0")]
public static class GetMediaEndpoint
{
    [WolverineGet("/media/{mediaId:guid}")]
    public static async Task<IResult> Get(
        Guid mediaId,
        IAmazonS3 s3,
        IConfiguration configuration,
        MediaServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var media =
            await dbContext.MediaAssets
                .AsNoTracking()
                .Where(
                    media =>
                        media.Id == mediaId)
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
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (media is null)
        {
            return Results.NotFound();
        }

        var bucketName =
            configuration["S3:BucketName"]
            ?? throw new InvalidOperationException(
                "S3 bucket name was not found.");

        var presignedUrl =
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

        return Results.Ok(
            new
            {
                media.Id,
                media.OwnerUserId,
                media.FileName,
                media.ContentType,
                media.Size,
                media.CreatedAtUtc,

                Url =
                    presignedUrl
            });
    }
}