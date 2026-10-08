using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class ContentConcurrencyTests(BackendFixture fixture)
{
    [Fact]
    public async Task FailedSaveRollsBackInteractionVersionAndOutboxTogether()
    {
        var user = Guid.NewGuid();
        var id = await SeedPost(user, "rollback-test");
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        // Deliberately fail the version UPDATE after the interaction INSERT.
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Posts\" ADD CONSTRAINT test_revision_failure CHECK (\"Title\" <> 'rollback-test' OR \"Revision\" = 1)");
        try
        {
            using var failed = await fixture.Send(user, HttpMethod.Post, $"{id}/likes");
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.False(await db.PostLikes.AnyAsync(like => like.PostId == id));
            Assert.Equal(1, (await db.Posts.FindAsync(id))!.Revision);
            Assert.DoesNotContain(MetricsSink.Messages, message => message.PostId == id);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Posts\" DROP CONSTRAINT test_revision_failure");
        }
    }

    [Fact]
    public async Task RepeatedConcurrentLikeAndUnlikeAreIdempotent()
    {
        var user = Guid.NewGuid();
        var postId = await SeedPost(user);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Write(user, HttpMethod.Post, $"{postId}/likes")));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Write(user, HttpMethod.Delete, $"{postId}/likes")));

        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        Assert.Equal(0, await db.PostLikes.CountAsync(like => like.PostId == postId));
        Assert.Equal(3, (await db.Posts.FindAsync(postId))!.Revision);
        await WaitForMessages(postId, 2);
        Assert.Equal(2, MetricsSink.Messages.Count(message => message.PostId == postId));
    }

    [Fact]
    public async Task LikeAfterDeleteCannotChangeThePost()
    {
        var user = Guid.NewGuid();
        var postId = await SeedPost(user);
        await Write(user, HttpMethod.Delete, postId.ToString());
        using var result = await fixture.Send(user, HttpMethod.Post, $"{postId}/likes");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, result.StatusCode);
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        Assert.False(await db.PostLikes.AnyAsync(like => like.PostId == postId));
    }

    private async Task<Guid> SeedPost(Guid author, string title = "concurrency")
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        var post = new Post(Guid.NewGuid(), author, title, "test content");
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        return post.Id;
    }

    private async Task Write(Guid user, HttpMethod method, string path)
    {
        using var response = await fixture.Send(user, method, path);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task WaitForMessages(Guid id, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (MetricsSink.Messages.Count(message => message.PostId == id) < count)
            await Task.Delay(50, timeout.Token);
    }

    internal static ClaimsPrincipal Principal(Guid user) => new(new ClaimsIdentity(
        [new Claim("sub", user.ToString())], "test"));
}
