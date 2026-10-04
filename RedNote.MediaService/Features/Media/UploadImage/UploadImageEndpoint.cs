using System.Security.Claims;
using Amazon.S3;
using Amazon.S3.Model;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using RedNote.MediaService.Domain.Media;
using RedNote.MediaService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.MediaService.Features.Media.UploadImage;

[ApiVersion("1.0")]
[Authorize]
public static class UploadImageEndpoint
{
    private const long MaxImageSize =
        10 * 1024 * 1024;

    private static readonly HashSet<string>
        AllowedContentTypes =
        [
            "image/jpeg",
            "image/png",
            "image/webp"
        ];

    [WolverinePost("/media/images")]
    public static async Task<IResult> Post(
        HttpRequest request,
        ClaimsPrincipal principal,
        IAmazonS3 s3,
        IConfiguration configuration,
        MediaServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        /*
         * =========================================================
         * 当前用户
         * =========================================================
         */

        var subject =
            principal.FindFirst(
                "sub")
            ?.Value;

        if (!Guid.TryParse(
                subject,
                out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        /*
         * =========================================================
         * multipart/form-data
         * =========================================================
         */

        if (!request.HasFormContentType)
        {
            return ValidationProblem(
                "file",
                "Image file is required.");
        }

        var form =
            await request.ReadFormAsync(
                cancellationToken);

        var file =
            form.Files.GetFile(
                "file");

        if (file is null)
        {
            return ValidationProblem(
                "file",
                "Image file is required.");
        }

        /*
         * =========================================================
         * 文件验证
         * =========================================================
         */

        if (file.Length <= 0)
        {
            return ValidationProblem(
                "file",
                "Image file is required.");
        }

        if (file.Length > MaxImageSize)
        {
            return ValidationProblem(
                "file",
                "Image cannot exceed 10 MB.");
        }

        if (
            !AllowedContentTypes.Contains(
                file.ContentType)
        )
        {
            return ValidationProblem(
                "file",
                "Only JPEG, PNG and WebP images are supported.");
        }

        /*
         * =========================================================
         * S3
         * =========================================================
         */

        var bucketName =
            configuration["S3:BucketName"]
            ?? throw new InvalidOperationException(
                "S3 bucket name was not found.");

        var mediaId =
            Guid.NewGuid();

        var extension =
            GetExtension(
                file.ContentType);

        var objectKey =
            $"images/{ownerUserId:N}/{mediaId:N}{extension}";

        await using var stream =
            file.OpenReadStream();

        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName =
                    bucketName,

                Key =
                    objectKey,

                InputStream =
                    stream,

                ContentType =
                    file.ContentType
            },
            cancellationToken);

        /*
         * =========================================================
         * 数据库
         * =========================================================
         */

        var mediaAsset =
            new MediaAsset(
                mediaId,
                ownerUserId,
                file.FileName,
                file.ContentType,
                file.Length,
                objectKey);

        dbContext.MediaAssets.Add(
            mediaAsset);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        /*
         * =========================================================
         * Response
         * =========================================================
         */

        return Results.Created(
            $"/api/v1/media/{mediaAsset.Id}",
            new
            {
                id =
                    mediaAsset.Id,

                mediaAsset.FileName,
                mediaAsset.ContentType,
                mediaAsset.Size,
                mediaAsset.ObjectKey,
                mediaAsset.CreatedAtUtc
            });
    }

    private static string GetExtension(
        string contentType)
    {
        return contentType switch
        {
            "image/jpeg" =>
                ".jpg",

            "image/png" =>
                ".png",

            "image/webp" =>
                ".webp",

            _ =>
                throw new InvalidOperationException(
                    "Unsupported image content type.")
        };
    }

    private static IResult ValidationProblem(
        string key,
        string message)
    {
        return Results.ValidationProblem(
            new Dictionary<
                string,
                string[]
            >
            {
                [key] =
                [
                    message
                ]
            });
    }
}