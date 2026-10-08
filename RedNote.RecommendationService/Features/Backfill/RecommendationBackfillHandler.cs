using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RedNote.Contracts.Recommendations;
using RedNote.RecommendationService.Features.Synchronization;
using RedNote.RecommendationService.Infrastructure.Gorse;
using RedNote.RecommendationService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace RedNote.RecommendationService.Features.Backfill;

public sealed record InitializeRecommendations;
public sealed record ImportGorseFeedback(Guid RunId, string Cursor = "");

public static class RecommendationBackfillHandler
{
    [Transactional]
    public static async Task Handle(InitializeRecommendations message, RecommendationDbContext db, IMessageBus bus, CancellationToken ct)
    {
        if (await db.Checkpoints.FindAsync(["initial-import"], ct) is not null) return;
        var run = Guid.NewGuid();
        db.Checkpoints.Add(new() { Name = "initial-import", RunId = run });
        await bus.PublishAsync(new ExportRecommendationCatalog(run));
    }
    [Transactional]
    public static async Task Handle(RecommendationCatalogExported message, RecommendationDbContext db, IMessageBus bus, CancellationToken ct)
    {
        var checkpoint = await db.Checkpoints.FindAsync(["initial-import"], ct);
        if (checkpoint is null || checkpoint.RunId != message.RunId || checkpoint.Phase != "catalog") return;
        // Ignore an older duplicated page marker; catalog/preference facts are versioned separately.
        if (checkpoint.AfterId is { } previous && message.AfterId is { } next
            && (next.CompareTo(previous) < 0 || next == previous && !message.Complete)) return;
        checkpoint.AfterId = message.AfterId;
        if (!message.Complete) await bus.PublishAsync(new ExportRecommendationCatalog(message.RunId, message.AfterId));
        else
        {
            checkpoint.Phase = "feedback";
            await bus.PublishAsync(new ImportGorseFeedback(message.RunId));
        }
    }
    [Transactional]
    public static async Task Handle(ImportGorseFeedback message, RecommendationDbContext db, GorseClient gorse, IMessageBus bus, CancellationToken ct)
    {
        // Read-only HTTP during one-time import; Gorse writes have a separate serial queue.
        var checkpoint = await db.Checkpoints.FindAsync(["initial-import"], ct);
        if (checkpoint is null || checkpoint.RunId != message.RunId || checkpoint.Phase != "feedback" || checkpoint.Cursor != message.Cursor) return;
        var page = await gorse.ListFeedback(message.Cursor, ct);
        var source = new List<RecommendationPreferenceIdentity>();
        foreach (var row in page.Feedback)
        {
            if (!Guid.TryParse(row.ItemId, out var post) || !Guid.TryParse(row.UserId, out var user)) continue;
            if (row.FeedbackType is "like" or "favorite") { source.Add(new(post, user, row.FeedbackType)); continue; }
            if (row.FeedbackType is not ("read" or "click")) continue;
            var state = await db.Feedback.FindAsync([post, user, row.FeedbackType], ct);
            if (state is not null && state.OccurredAtUtc >= row.Timestamp) continue;
            if (state is null) { state = new() { PostId = post, UserId = user, Type = row.FeedbackType }; db.Feedback.Add(state); }
            state.IsActive = true; state.OccurredAtUtc = row.Timestamp; state.Version++;
            await bus.PublishAsync(new SyncRecommendationItem(post));
        }
        if (source.Count > 0) await bus.PublishAsync(new ReconcileRecommendationPreferences(source.ToArray()));
        checkpoint.Cursor = page.Cursor;
        if (string.IsNullOrEmpty(page.Cursor)) { checkpoint.Phase = "complete"; await bus.PublishAsync(new ReconcileRecommendationPage()); }
        else await bus.PublishAsync(new ImportGorseFeedback(message.RunId, page.Cursor));
    }
}

public sealed class RecommendationBootstrap(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, IOptions<RecommendationOptions> options,
    ILogger<RecommendationBootstrap> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                if (options.Value.Initialize) await bus.PublishAsync(new InitializeRecommendations());
                await bus.PublishAsync(new ReconcileRecommendationPage());
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Unable to queue recommendation initialization"); await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        }
    }
}
