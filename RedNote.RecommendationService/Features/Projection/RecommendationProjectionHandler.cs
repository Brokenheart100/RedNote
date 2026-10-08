using Microsoft.EntityFrameworkCore;
using RedNote.Contracts.Recommendations;
using RedNote.RecommendationService.Features.Synchronization;
using RedNote.RecommendationService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace RedNote.RecommendationService.Features.Projection;

public static class RecommendationProjectionHandler
{
    [Transactional]
    public static async Task Handle(RecommendationItemStateChanged message, RecommendationDbContext db, IMessageBus bus, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([message.PostId], ct);
        if (item is not null && (message.Revision <= item.SourceRevision || !item.IsPublished)) return;
        if (item is null) { item = new() { PostId = message.PostId }; db.Items.Add(item); }
        item.AuthorUserId = message.AuthorUserId; item.Tags = message.Tags; item.CreatedAtUtc = message.CreatedAtUtc;
        item.IsPublished = message.IsPublished; item.IsHidden = message.IsHidden; item.SourceRevision = message.Revision;
        await bus.PublishAsync(new SyncRecommendationItem(message.PostId));
    }

    [Transactional]
    public static async Task Handle(RecommendationPreferenceStateChanged message, RecommendationDbContext db, IMessageBus bus, CancellationToken ct)
    {
        if (message.Kind is not ("like" or "favorite")) throw new InvalidOperationException("Invalid source preference kind.");
        var state = await db.Feedback.FindAsync([message.PostId, message.UserId, message.Kind], ct);
        if (state is not null && message.Revision <= state.SourceRevision) return;
        if (state is null)
        {
            state = new() { PostId = message.PostId, UserId = message.UserId, Type = message.Kind };
            db.Feedback.Add(state);
        }
        state.SourceRevision = message.Revision; state.IsActive = message.IsActive;
        state.OccurredAtUtc = message.OccurredAtUtc; state.Version++;
        await bus.PublishAsync(new SyncRecommendationItem(message.PostId));
    }
}
