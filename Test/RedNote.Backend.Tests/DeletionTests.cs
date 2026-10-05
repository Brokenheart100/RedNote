using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Delete;
using RedNote.ContentService.Features.Posts.DeleteComment;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class DeletionTests(BackendFixture fixture)
{
    [Fact]
    public async Task RootDeletionSoftDeletesRepliesAndUpdatesMetricsOnlyOnce()
    {
        var owner = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var root = new PostComment(Guid.NewGuid(), postId, owner, "root", null);
        var reply = new PostComment(Guid.NewGuid(), postId, Guid.NewGuid(), "reply", root.Id);
        var remaining = new PostComment(Guid.NewGuid(), postId, Guid.NewGuid(), "remaining", null);
        await Seed(postId, owner, root, reply, remaining);
        await DeleteComment(postId, root.Id, owner, 204);
        await DeleteComment(postId, root.Id, owner, 204);
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        Assert.Equal(PostCommentStatus.Deleted, (await db.PostComments.FindAsync(root.Id))!.Status);
        Assert.Equal(PostCommentStatus.Deleted, (await db.PostComments.FindAsync(reply.Id))!.Status);
        Assert.Equal(PostCommentStatus.Published, (await db.PostComments.FindAsync(remaining.Id))!.Status);
        Assert.Equal(2, (await db.Posts.FindAsync(postId))!.Revision);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!MetricsSink.Messages.Any(message => message.PostId == postId))
            await Task.Delay(50, timeout.Token);
        var message = Assert.Single(MetricsSink.Messages, message => message.PostId == postId);
        Assert.Equal(1, message.CommentCount);
    }

    [Fact]
    public async Task ReplyDeletionKeepsItsParentAndSiblings()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var root = new PostComment(Guid.NewGuid(), id, owner, "root", null);
        var reply = new PostComment(Guid.NewGuid(), id, owner, "reply", root.Id);
        var sibling = new PostComment(Guid.NewGuid(), id, owner, "sibling", root.Id);
        await Seed(id, owner, root, reply, sibling);
        await DeleteComment(id, reply.Id, owner, 204);
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        Assert.Equal(2, await db.PostComments.CountAsync(comment => comment.PostId == id
            && comment.Status == PostCommentStatus.Published));
        Assert.Equal(PostCommentStatus.Published, (await db.PostComments.FindAsync(root.Id))!.Status);
    }

    [Fact]
    public async Task NonAuthorCannotDeletePostOrComment()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var id = Guid.NewGuid();
        var root = new PostComment(Guid.NewGuid(), id, owner, "root", null);
        await Seed(id, owner, root);
        await DeleteComment(id, root.Id, stranger, 403);
        using var scope = fixture.Host.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<ContentServiceDbContext>>();
        var result = await DeletePostEndpoint.Delete(id, ContentConcurrencyTests.Principal(stranger), outbox, default);
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
        Assert.Equal(PostStatus.Published, (await outbox.DbContext.Posts.FindAsync(id))!.Status);
        Assert.Equal(1, (await outbox.DbContext.Posts.FindAsync(id))!.Revision);
    }

    private async Task Seed(Guid id, Guid owner, params PostComment[] comments)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>();
        db.Posts.Add(new Post(id, owner, "delete test", "body"));
        db.PostComments.AddRange(comments);
        await db.SaveChangesAsync();
    }

    private async Task DeleteComment(Guid postId, Guid commentId, Guid user, int expectedStatus)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<ContentServiceDbContext>>();
        var result = await DeletePostCommentEndpoint.Delete(postId, commentId,
            ContentConcurrencyTests.Principal(user), outbox, default);
        if (expectedStatus == 403)
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
        else
            Assert.Equal(expectedStatus, ((IStatusCodeHttpResult)result).StatusCode);
    }
}
