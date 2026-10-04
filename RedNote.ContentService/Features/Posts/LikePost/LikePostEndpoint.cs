using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.LikePost;

[ApiVersion("1.0")]
[Authorize]
public static class LikePostEndpoint
{
    [WolverinePost("/posts/{postId:guid}/likes")]
    public static async Task<IResult> Post(
        Guid postId,
        ClaimsPrincipal principal,
        [FromServices]
        IDbContextOutbox<ContentServiceDbContext> outbox,
        CancellationToken cancellationToken)
    {
        var dbContext =
            outbox.DbContext;

        var subject =
            principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(
                subject,
                out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var postExists =
            await dbContext.Posts
                .AsNoTracking()
                .AnyAsync(
                    post =>
                        post.Id == postId
                        && post.Status !=
                            PostStatus.Deleted,
                    cancellationToken);

        if (!postExists)
        {
            return Results.NotFound();
        }

        var alreadyLiked =
            await dbContext.PostLikes
                .AnyAsync(
                    like =>
                        like.PostId == postId
                        && like.UserId ==
                            currentUserId,
                    cancellationToken);

        if (alreadyLiked)
        {
            return Results.NoContent();
        }

        dbContext.PostLikes.Add(
            new PostLike(
                postId,
                currentUserId));

        /*
         * 新 Like 尚未 SaveChanges，
         * 所以数据库计数少 1。
         */

        var existingLikeCount =
            await dbContext.PostLikes
                .AsNoTracking()
                .CountAsync(
                    like =>
                        like.PostId == postId,
                    cancellationToken);

        var likeCount =
            existingLikeCount + 1;

        var commentCount =
            await dbContext.PostComments
                .AsNoTracking()
                .CountAsync(
                    comment =>
                        comment.PostId == postId
                        && comment.Status ==
                            PostCommentStatus.Published,
                    cancellationToken);

        await outbox.PublishAsync(
            new PostMetricsChanged(
                postId,
                likeCount,
                commentCount));

        await outbox
            .SaveChangesAndFlushMessagesAsync(
                cancellationToken);

        return Results.NoContent();
    }
}