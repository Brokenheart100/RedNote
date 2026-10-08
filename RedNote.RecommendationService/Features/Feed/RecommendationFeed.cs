using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using RedNote.Contracts.Content;
using RedNote.Contracts.Recommendations;
using RedNote.RecommendationService.Infrastructure.Content;
using RedNote.RecommendationService.Infrastructure.Gorse;
using RedNote.RecommendationService.Infrastructure.Persistence;

namespace RedNote.RecommendationService.Features.Feed;

public sealed record RecommendationSnapshot(string Owner, Guid[] Ids, string Strategy);

public sealed class RecommendationFeed(GorseClient gorse, IDistributedCache cache, RecommendationDbContext db,
    ContentClient content, ILogger<RecommendationFeed> logger)
{
    public static string Owner(Guid? user) => user?.ToString() ?? "anonymous";
    private static DistributedCacheEntryOptions Ttl() => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) };
    public async Task<RecommendationSnapshot?> GetSnapshot(Guid id, CancellationToken ct)
    {
        var json = await cache.GetStringAsync($"feed:{id:N}", ct);
        return json is null ? null : JsonSerializer.Deserialize<RecommendationSnapshot>(json);
    }
    public async Task<bool> WasDelivered(Guid request, Guid post, CancellationToken ct) =>
        await cache.GetStringAsync($"delivered:{request:N}:{post:N}", ct) is not null;

    public async Task<RecommendationFeedResponse> Get(Guid? user, int size, string? cursor, string? authorization, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        ct = budget.Token;
        Guid requestId;
        var offset = 0;
        RecommendationSnapshot snapshot;
        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out requestId)
                || !int.TryParse(parts[1], out offset) || offset < 0 || offset > 500)
                throw new BadHttpRequestException("Invalid recommendation cursor.");
            snapshot = await GetSnapshot(requestId, ct) ?? throw new BadHttpRequestException("Feed expired. Refresh.", 410);
            if (snapshot.Owner != Owner(user)) throw new BadHttpRequestException("Invalid cursor owner.", 403);
            if (offset > snapshot.Ids.Length) throw new BadHttpRequestException("Invalid recommendation cursor.");
        }
        else
        {
            requestId = Guid.NewGuid();
            Guid[] ids = [];
            var strategy = "latest";
            if (user is { } id)
            {
                try
                {
                    using var queryBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    queryBudget.CancelAfter(TimeSpan.FromSeconds(3));
                    ids = (await gorse.Recommend(id, queryBudget.Token)).Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty)
                        .Where(x => x != Guid.Empty).Distinct().Take(500).ToArray();
                    if (ids.Length > 0) strategy = "gorse";
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
                { logger.LogWarning("Gorse query failed ({ErrorType}); using latest candidates", ex.GetType().Name); }
            }
            if (ids.Length == 0)
                ids = await db.Items.AsNoTracking().Where(x => x.IsPublished && !x.IsHidden)
                    .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.PostId).Take(500).Select(x => x.PostId).ToArrayAsync(ct);
            snapshot = new(Owner(user), ids, strategy);
            await cache.SetStringAsync($"feed:{requestId:N}", JsonSerializer.Serialize(snapshot), Ttl(), ct);
        }
        var items = new List<PostResponse>();
        // Bound external work. Each scanned chunk is no larger than this page's remaining slots.
        for (var attempt = 0; offset < snapshot.Ids.Length && items.Count < size && attempt < 10; attempt++)
        {
            var ids = snapshot.Ids.Skip(offset).Take(size - items.Count).ToArray();
            var found = (await content.Batch(ids, authorization, ct)).ToDictionary(x => x.Id);
            items.AddRange(ids.Where(found.ContainsKey).Select(id => found[id]));
            offset += ids.Length;
        }
        foreach (var item in items)
            await cache.SetStringAsync($"delivered:{requestId:N}:{item.Id:N}", "1", Ttl(), ct);
        var more = offset < snapshot.Ids.Length;
        return new(items, more ? $"{requestId:N}:{offset}" : null, more, requestId.ToString(), snapshot.Strategy);
    }
}
