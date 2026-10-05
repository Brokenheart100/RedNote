using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using RedNote.MediaService.Infrastructure.Persistence;

namespace RedNote.MediaService.Features.Media.Common;

// Completes failed compensation, including a process crash between S3 and DB.
// Only old objects without a media record are removed; unused but registered
// uploads are deliberately retained because posts/avatars may reference them.
internal sealed class OrphanedUploadCleanup(
    IServiceScopeFactory scopeFactory, IAmazonS3 s3, IConfiguration configuration,
    ILogger<OrphanedUploadCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Orphaned upload cleanup failed; will retry next hour.");
            }
        }
    }

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var bucket = configuration["S3:BucketName"]!;
        var cutoff = DateTime.UtcNow.AddDays(-1);
        string? continuationToken = null;
        do
        {
            var page = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket, Prefix = "images/", MaxKeys = 1000,
                ContinuationToken = continuationToken
            }, cancellationToken);
            var candidates = (page.S3Objects ?? []).Where(item => item.LastModified < cutoff)
                .Select(item => item.Key).ToArray();
            if (candidates.Length > 0)
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MediaServiceDbContext>();
                var registered = await db.MediaAssets.AsNoTracking()
                    .Where(asset => candidates.Contains(asset.ObjectKey))
                    .Select(asset => asset.ObjectKey).ToListAsync(cancellationToken);
                var registeredKeys = registered.ToHashSet(StringComparer.Ordinal);
                foreach (var key in candidates.Where(key => !registeredKeys.Contains(key)))
                {
                    await s3.DeleteObjectAsync(new DeleteObjectRequest
                    {
                        BucketName = bucket, Key = key
                    }, cancellationToken);
                    logger.LogInformation("Removed orphaned upload {ObjectKey}", key);
                }
            }
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (continuationToken is not null);
    }
}
