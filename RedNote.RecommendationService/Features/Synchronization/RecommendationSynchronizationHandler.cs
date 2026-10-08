using Microsoft.EntityFrameworkCore;
using RedNote.RecommendationService.Infrastructure.Gorse;
using RedNote.RecommendationService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace RedNote.RecommendationService.Features.Synchronization;

public sealed record SyncRecommendationItem(Guid PostId);
public sealed record ReconcileRecommendationPage(Guid? AfterId = null);

public static class RecommendationSynchronizationHandler
{
    // HTTP is deliberately outside an EF transaction. One active sync consumer owns this queue.
    public static async Task Handle(SyncRecommendationItem message, RecommendationDbContext db, GorseClient gorse, IMessageBus bus, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(x => x.PostId == message.PostId, ct);
        if (item is null) return; // The catalog event will wake synchronization when it arrives.
        var feedback = await db.Feedback.AsNoTracking().Where(x => x.PostId == item.PostId).ToArrayAsync(ct);
        if (!item.IsPublished) await gorse.Delete(item.PostId, ct);
        else
        {
            await gorse.Upsert(new(item.PostId.ToString(), item.IsHidden, item.CreatedAtUtc,
                new { tags = item.Tags, author = item.AuthorUserId.ToString() }), ct);
            foreach (var batch in feedback.Where(x => x.IsActive).Chunk(100))
                await gorse.SetFeedback(batch.Select(x => new GorseFeedback(x.Type, x.UserId.ToString(), x.PostId.ToString(), x.OccurredAtUtc)).ToArray(), ct);
            foreach (var inactive in feedback.Where(x => !x.IsActive))
                await gorse.DeleteFeedback(item.PostId, inactive.UserId, inactive.Type, ct);
        }
        await db.Items.Where(x => x.PostId == item.PostId && x.SourceRevision == item.SourceRevision)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAcknowledgedRevision, item.SourceRevision), ct);
        foreach (var state in feedback)
            await db.Feedback.Where(x => x.PostId == state.PostId && x.UserId == state.UserId && x.Type == state.Type && x.Version == state.Version)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAcknowledgedRevision, state.Version), ct);
        if (await db.Items.AnyAsync(x => x.PostId == item.PostId && x.SourceRevision != x.LastAcknowledgedRevision, ct)
            || await db.Feedback.AnyAsync(x => x.PostId == item.PostId && x.Version != x.LastAcknowledgedRevision, ct))
            await bus.PublishAsync(new SyncRecommendationItem(item.PostId));
    }

    [Transactional]
    public static async Task Handle(ReconcileRecommendationPage message, RecommendationDbContext db, IMessageBus bus, CancellationToken ct)
    {
        // Include acknowledged records and tombstones: an old timed-out HTTP write can finish late.
        var ids = await db.Items.Where(x => message.AfterId == null || x.PostId.CompareTo(message.AfterId.Value) > 0)
            .OrderBy(x => x.PostId).Select(x => x.PostId).Take(100).ToArrayAsync(ct);
        foreach (var id in ids) await bus.PublishAsync(new SyncRecommendationItem(id));
        if (ids.Length == 100) await bus.PublishAsync(new ReconcileRecommendationPage(ids[^1]));
        if (message.AfterId is null)
            await db.Receipts.Where(x => x.RecordedAtUtc < DateTimeOffset.UtcNow.AddDays(-7)).ExecuteDeleteAsync(ct);
    }
}
