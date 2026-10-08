using Microsoft.EntityFrameworkCore;
using RedNote.MediaService.Infrastructure.Persistence;

namespace RedNote.MediaService.Features.Media.Common;

public sealed class MediaQueryService(
    MediaServiceDbContext dbContext,
    MediaUrlSigner signer,
    IConfiguration configuration)
{
    public async Task<IReadOnlyList<MediaReadModel>> GetBatchAsync(
        IEnumerable<Guid>? requestedIds,
        CancellationToken cancellationToken)
    {
        var mediaIds = requestedIds?.Distinct().ToArray() ?? [];
        if (mediaIds.Length == 0)
            return [];

        var bucketName = configuration["S3:BucketName"]
            ?? throw new InvalidOperationException("S3 bucket name was not found.");

        var mediaAssets = await dbContext.MediaAssets
            .AsNoTracking()
            .Where(media => mediaIds.Contains(media.Id))
            .Select(media => new
            {
                media.Id, media.OwnerUserId, media.FileName,
                media.ContentType, media.Size, media.ObjectKey, media.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var items = new List<MediaReadModel>(mediaAssets.Count);
        foreach (var media in mediaAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = await signer.GetDownloadUrlAsync(bucketName, media.ObjectKey);
            items.Add(new MediaReadModel(
                media.Id, media.OwnerUserId, media.FileName, media.ContentType,
                media.Size, media.CreatedAtUtc, url));
        }
        return items;
    }
}

public sealed record MediaReadModel(
    Guid Id,
    Guid OwnerUserId,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset CreatedAtUtc,
    string Url);
