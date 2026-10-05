using OpenSearch.Client;
using RedNote.SearchService.Domain.Posts;

namespace RedNote.SearchService.Infrastructure.OpenSearch;

internal static class PostProjectionWriter
{
    // Metadata and metrics have independent versions: a newer metrics event can
    // precede an older title update without losing either change.
    private const string SnapshotScript = """
        if (ctx._source.isDeleted == true) { ctx.op = 'noop'; }
        else {
            boolean changed = false;
            if (ctx.op == 'create' || ctx._source.metadataRevision == null || params.revision > ctx._source.metadataRevision) {
                ctx._source.id = params.document.id;
                ctx._source.authorUserId = params.document.authorUserId;
                ctx._source.title = params.document.title;
                ctx._source.content = params.document.content;
                ctx._source.tags = params.document.tags;
                ctx._source.createdAtUtc = params.document.createdAtUtc;
                ctx._source.updatedAtUtc = params.document.updatedAtUtc;
                ctx._source.metadataRevision = params.revision;
                changed = true;
            }
            if (ctx.op == 'create' || ctx._source.metricsRevision == null || params.revision > ctx._source.metricsRevision) {
                ctx._source.likeCount = params.document.likeCount;
                ctx._source.commentCount = params.document.commentCount;
                ctx._source.metricsRevision = params.revision;
                changed = true;
            }
            if (!changed) { ctx.op = 'noop'; }
        }
        """;

    private const string MetricsScript = """
        if (ctx._source.isDeleted == true) { ctx.op = 'noop'; }
        else if (ctx.op == 'create' || ctx._source.metricsRevision == null || params.revision > ctx._source.metricsRevision) {
            ctx._source.id = params.id;
            ctx._source.likeCount = params.likeCount;
            ctx._source.commentCount = params.commentCount;
            ctx._source.metricsRevision = params.revision;
        } else { ctx.op = 'noop'; }
        """;

    // A deleted post cannot be restored in ContentService. Keep a minimal,
    // permanent tombstone so even a late publish event cannot recreate it.
    private const string DeleteScript = """
        if (ctx._source.isDeleted == true) { ctx.op = 'noop'; }
        else {
            ctx._source.clear();
            ctx._source.id = params.id;
            ctx._source.isDeleted = true;
            ctx._source.metadataRevision = params.revision;
        }
        """;

    internal static Task WriteSnapshotAsync(IOpenSearchClient client, PostSearchDocument document,
        long revision, CancellationToken cancellationToken) => ApplyAsync(client, document.Id, SnapshotScript,
            new Dictionary<string, object> { ["revision"] = revision, ["document"] = new
            {
                id = document.Id, authorUserId = document.AuthorUserId, title = document.Title,
                content = document.Content, tags = document.Tags, likeCount = document.LikeCount,
                commentCount = document.CommentCount, createdAtUtc = document.CreatedAtUtc,
                updatedAtUtc = document.UpdatedAtUtc
            } }, cancellationToken);

    internal static Task WriteMetricsAsync(IOpenSearchClient client, Guid id, int likeCount,
        int commentCount, long revision, CancellationToken cancellationToken) => ApplyAsync(client, id,
            MetricsScript, new Dictionary<string, object>
            {
                ["id"] = id, ["revision"] = revision,
                ["likeCount"] = likeCount, ["commentCount"] = commentCount
            }, cancellationToken);

    internal static Task DeleteAsync(IOpenSearchClient client, Guid id, long revision,
        CancellationToken cancellationToken) => ApplyAsync(client, id, DeleteScript,
            new Dictionary<string, object> { ["id"] = id, ["revision"] = revision }, cancellationToken);

    private static async Task ApplyAsync(IOpenSearchClient client, Guid id, string script,
        Dictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        var response = await client.UpdateAsync<PostSearchDocument, object>(id, descriptor => descriptor
            .Index(OpenSearchIndexInitializer.PostIndexName)
            .RetryOnConflict(5)
            .Script(s => s.Source(script).Params(parameters))
            .ScriptedUpsert()
            .Upsert(new PostSearchDocument { Id = id, MetadataRevision = -1, MetricsRevision = -1 }),
            cancellationToken);

        if (!response.IsValid)
            throw new InvalidOperationException($"Failed to project post '{id}'. {response.DebugInformation}");
    }
}
