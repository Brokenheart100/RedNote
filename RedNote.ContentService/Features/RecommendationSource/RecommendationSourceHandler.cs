using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Recommendations;
using Wolverine;
using Wolverine.Attributes;

namespace RedNote.ContentService.Features.RecommendationSource;

public static class RecommendationSourceState
{
    public static RecommendationItemStateChanged RecommendationState(this Post post, IEnumerable<string> tags) =>
        new(post.Id, post.AuthorUserId, tags.ToArray(), post.CreatedAtUtc, post.Status == PostStatus.Published, post.IsHidden, post.Revision);
}

public static class RecommendationSourceHandler
{
    [Transactional]
    public static async Task Handle(ExportRecommendationCatalog message, ContentServiceDbContext db, IMessageBus bus, CancellationToken ct)
    {
        var ids = await db.Posts.AsNoTracking().Where(x => message.AfterId == null || x.Id.CompareTo(message.AfterId.Value) > 0)
            .OrderBy(x => x.Id).Take(100).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            // Hold the source row only for EF reads + outbox commit, never for Gorse HTTP.
            var post = (await db.LockPostForWriteAsync(id, ct))!;
            var tags = await db.PostTags.Where(x => x.PostId == id).Select(x => x.Name).ToArrayAsync(ct);
            await bus.PublishAsync(post.RecommendationState(tags));
            foreach (var like in await db.PostLikes.Where(x => x.PostId == id).ToArrayAsync(ct))
                await bus.PublishAsync(new RecommendationPreferenceStateChanged(id, like.UserId, "like", true, post.Revision, like.CreatedAtUtc));
            foreach (var favorite in await db.PostFavorites.Where(x => x.PostId == id).ToArrayAsync(ct))
                await bus.PublishAsync(new RecommendationPreferenceStateChanged(id, favorite.UserId, "favorite", true, post.Revision, favorite.CreatedAtUtc));
        }
        await bus.PublishAsync(new RecommendationCatalogExported(message.RunId, ids.LastOrDefault() is var last && last != Guid.Empty ? last : message.AfterId, ids.Length < 100));
    }

    [Transactional]
    public static async Task Handle(ReconcileRecommendationPreferences message, ContentServiceDbContext db, IMessageBus bus, CancellationToken ct)
    {
        if (message.Items.Length > 100 || message.Items.Any(x => x.Kind is not ("like" or "favorite")))
            throw new InvalidOperationException("Invalid recommendation reconciliation request.");
        foreach (var group in message.Items.Distinct().GroupBy(x => x.PostId).OrderBy(x => x.Key))
        {
            var post = await db.LockPostForWriteAsync(group.Key, ct);
            if (post is null)
            {
                await bus.PublishAsync(new RecommendationItemStateChanged(group.Key, Guid.Empty, [], DateTimeOffset.UtcNow, false, true, long.MaxValue));
                continue;
            }
            foreach (var identity in group)
            {
                DateTimeOffset? at = identity.Kind == "like"
                    ? await db.PostLikes.Where(x => x.PostId == identity.PostId && x.UserId == identity.UserId).Select(x => (DateTimeOffset?)x.CreatedAtUtc).SingleOrDefaultAsync(ct)
                    : await db.PostFavorites.Where(x => x.PostId == identity.PostId && x.UserId == identity.UserId).Select(x => (DateTimeOffset?)x.CreatedAtUtc).SingleOrDefaultAsync(ct);
                await bus.PublishAsync(new RecommendationPreferenceStateChanged(identity.PostId, identity.UserId, identity.Kind,
                    at is not null && post.Status == PostStatus.Published, post.Revision, at ?? DateTimeOffset.UtcNow));
            }
        }
    }
}
