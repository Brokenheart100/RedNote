using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Features.Posts.Update;
using RedNote.ContentService.Infrastructure.Persistence;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class PostUpdateTransactionTests(BackendFixture fixture)
{
    [Fact]
    public async Task ResponseShowsUpdatedTagsAndNullPreservesWhileEmptyClears()
    {
        var owner = Guid.NewGuid();
        var id = await Seed(owner);
        foreach (var tags in new IReadOnlyList<string>?[] { ["new"], null, [] })
        {
            using var response = await fixture.Send(owner, HttpMethod.Patch, id.ToString(),
                new UpdatePostRequest("updated", "updated body", tags));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var post = await response.Content.ReadFromJsonAsync<PostResponse>();
            IReadOnlyList<string> expected = tags is { Count: 0 } ? [] : ["new"];
            Assert.Equal(expected, post!.Tags);
        }
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        Assert.Equal(4, (await db.Posts.FindAsync(id))!.Revision);
        Assert.Empty(await db.PostTags.Where(tag => tag.PostId == id).ToListAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (MetricsSink.Updates.Count(message => message.PostId == id) < 3)
            await Task.Delay(50, timeout.Token);
        Assert.Equal(3, MetricsSink.Updates.Count(message => message.PostId == id));
    }

    [Fact]
    public async Task ResponseFailureRollsBackEvenAfterIntermediateSave()
    {
        var owner = Guid.NewGuid();
        var media = Guid.NewGuid();
        var id = await Seed(owner, media);
        fixture.Media.Failures[media] = true;
        try
        {
            using var response = await fixture.Send(owner, HttpMethod.Patch, id.ToString(),
                new UpdatePostRequest("updated", "updated body", ["new"]));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            using var scope = fixture.Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
            var post = (await db.Posts.FindAsync(id))!;
            Assert.Equal("original", post.Title);
            Assert.Equal(1, post.Revision);
            Assert.Equal("old", (await db.PostTags.SingleAsync(tag => tag.PostId == id)).Name);
            Assert.DoesNotContain(MetricsSink.Updates, message => message.PostId == id);
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM wolverine.wolverine_incoming_envelopes WHERE position(convert_to({id.ToString()}, 'UTF8') in body) > 0").SingleAsync());
        }
        finally
        {
            fixture.Media.Failures.TryRemove(media, out _);
        }
    }

    [Fact]
    public async Task NonOwnerCannotUpdateOrPublishAnEvent()
    {
        var id = await Seed(Guid.NewGuid());
        using var response = await fixture.Send(Guid.NewGuid(), HttpMethod.Patch, id.ToString(),
            new UpdatePostRequest("updated", "updated body", ["new"]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        Assert.Equal(1, (await db.Posts.FindAsync(id))!.Revision);
        Assert.DoesNotContain(MetricsSink.Updates, message => message.PostId == id);
    }

    private async Task<Guid> Seed(Guid owner, Guid? media = null)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        var id = Guid.NewGuid();
        db.Posts.Add(new Post(id, owner, "original", "body"));
        db.PostTags.Add(new PostTag(id, "old"));
        if (media.HasValue) db.PostMedia.Add(new PostMedia(id, media.Value, 0));
        await db.SaveChangesAsync();
        return id;
    }
}
