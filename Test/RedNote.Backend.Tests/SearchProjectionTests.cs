using OpenSearch.Net;
using RedNote.Contracts.Content;
using RedNote.SearchService.Domain.Posts;
using RedNote.SearchService.Features.Search.Posts.ProjectPostDeleted;
using RedNote.SearchService.Features.Search.Posts.ProjectPostMetricsChanged;
using RedNote.SearchService.Features.Search.Posts.ProjectPostPublished;
using RedNote.SearchService.Features.Search.Posts.ProjectPostUpdated;
using RedNote.SearchService.Infrastructure.OpenSearch;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class SearchProjectionTests(BackendFixture fixture)
{
    [Fact]
    public async Task OlderMetadataAndMetricsCannotOverwriteNewerValues()
    {
        var id = Guid.NewGuid();
        await Update(id, "new title", 5, 5);
        await Update(id, "old title", 2, 2);
        await Metrics(id, 7, 7);
        await Metrics(id, 6, 6);
        await Update(id, "new title", 5, 5); // Duplicate must not reset metrics.
        var doc = await Get(id);
        Assert.Equal("new title", doc.Title);
        Assert.Equal(7, doc.LikeCount);
        Assert.Equal(5, doc.MetadataRevision);
        Assert.Equal(7, doc.MetricsRevision);
    }

    [Fact]
    public async Task MetricsArrivingBeforePublishAreRetained()
    {
        var id = Guid.NewGuid();
        await Metrics(id, 5, 5);
        var now = DateTimeOffset.UtcNow;
        await PostPublishedHandler.Handle(new PostPublished(id, Guid.NewGuid(), "published", "body", [],
            0, 0, now, now, 1), fixture.Search, default);
        var doc = await Get(id);
        Assert.Equal("published", doc.Title);
        Assert.Equal(5, doc.LikeCount);
        Assert.Equal(1, doc.MetadataRevision);
    }

    [Fact]
    public async Task DeleteBeforePublishLeavesATombstoneAndNeverRestoresThePost()
    {
        var id = Guid.NewGuid();
        await PostDeletedHandler.Handle(new PostDeleted(id, DateTimeOffset.UtcNow, 9), fixture.Search, default);
        await Update(id, "late update", 8, 8);
        await Metrics(id, 7, 7);
        var now = DateTimeOffset.UtcNow;
        await PostPublishedHandler.Handle(new PostPublished(id, Guid.NewGuid(), "late publish", "body", [],
            0, 0, now, now, 1), fixture.Search, default);
        Assert.True((await Get(id)).IsDeleted);
        await fixture.Search.Indices.RefreshAsync(OpenSearchIndexInitializer.PostIndexName);
        var search = await fixture.Search.SearchAsync<PostSearchDocument>(request => request
            .Query(query => query.Bool(boolean => boolean
                .Must(must => must.Ids(ids => ids.Values(id)))
                .MustNot(excluded => excluded.Term(term => term.Field(doc => doc.IsDeleted).Value(true))))));
        Assert.True(search.IsValid, search.DebugInformation);
        Assert.Empty(search.Documents);
    }

    [Fact]
    public async Task ParallelProjectionRetriesConflictsWithoutLosingTheHighestVersion()
    {
        var id = Guid.NewGuid();
        await Update(id, "parallel", 1, 0);
        await Task.WhenAll(Enumerable.Range(2, 8).Select(version => Metrics(id, version, version)));
        Assert.Equal(9, (await Get(id)).LikeCount);
    }

    private Task Update(Guid id, string title, long revision, int count)
    {
        var now = DateTimeOffset.UtcNow;
        return PostUpdatedHandler.Handle(new PostUpdated(id, Guid.NewGuid(), title, "body", [], count,
            count, now, now, revision), fixture.Search, default);
    }

    private Task Metrics(Guid id, int count, long revision) => PostMetricsChangedHandler.Handle(
        new PostMetricsChanged(id, count, count, revision), fixture.Search, default);

    private async Task<PostSearchDocument> Get(Guid id)
    {
        var result = await fixture.Search.GetAsync<PostSearchDocument>(id);
        Assert.True(result.IsValid && result.Found, result.DebugInformation);
        return result.Source;
    }
}
