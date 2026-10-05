using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.DeleteComment;

[ApiVersion("1.0")]
[Authorize]
public static class DeletePostCommentEndpoint
{
    [WolverineDelete("/posts/{postId:guid}/comments/{commentId:guid}")]
    public static async Task<IResult> Delete(
        Guid postId,
        Guid commentId,
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var post = await PostWriteLock.AcquireAsync(dbContext, postId, cancellationToken);
        if (post is null || post.Status == PostStatus.Deleted)
            return Results.NotFound();

        var comment =
            await dbContext.PostComments
                .SingleOrDefaultAsync(
                    comment =>
                        comment.Id == commentId
                        && comment.PostId ==
                            postId,
                    cancellationToken);

        if (comment is null)
        {
            return Results.NotFound();
        }

        if (comment.AuthorUserId !=
            currentUserId)
        {
            return Results.Forbid();
        }

        if (comment.Status ==
            PostCommentStatus.Deleted)
        {
            return Results.NoContent();
        }

        comment.Delete();

        // A removed root must not leave live, unreachable replies in statistics.
        var removedCount = 1;
        if (comment.ParentCommentId is null)
        {
            var replies = await dbContext.PostComments.Where(reply =>
                reply.PostId == postId && reply.ParentCommentId == commentId
                && reply.Status == PostCommentStatus.Published).ToListAsync(cancellationToken);
            foreach (var reply in replies) reply.Delete();
            removedCount += replies.Count;
        }

        /*
         * Metrics
         *
         * 当前 Comment 尚未真正保存 Deleted 状态，
         * 所以数据库统计仍然包含它。
         */

        var likeCount =
            await dbContext.PostLikes
                .AsNoTracking()
                .CountAsync(
                    like =>
                        like.PostId == postId,
                    cancellationToken);

        var existingCommentCount =
            await dbContext.PostComments
                .AsNoTracking()
                .CountAsync(
                    existingComment =>
                        existingComment.PostId ==
                            postId
                        && existingComment.Status ==
                            PostCommentStatus.Published,
                    cancellationToken);

        var commentCount =
            Math.Max(
                0,
                existingCommentCount - removedCount);

        /*
         * Transactional Outbox
         */

        post.RecordMetricsChange();
        await outbox.PublishAsync(
            new PostMetricsChanged(
                postId,
                likeCount,
                commentCount,
                post.Revision));

        await outbox
            .SaveChangesAndFlushMessagesAsync(
                cancellationToken);

        return Results.NoContent();
    }
}
