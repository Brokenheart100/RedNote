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
                existingCommentCount - 1);

        /*
         * Transactional Outbox
         */

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