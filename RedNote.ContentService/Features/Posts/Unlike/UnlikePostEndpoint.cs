using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Unlike;

[ApiVersion("1.0")]
[Authorize]
public static class UnlikePostEndpoint
{
    [WolverineDelete("/posts/{postId:guid}/likes")]
    [Transactional]
    public static async Task<IResult> Delete(
        Guid postId,
        ClaimsPrincipal principal,
        [FromServices]
        ContentServiceDbContext dbContext,
        [FromServices] IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var subject =
            principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(
                subject,
                out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var post = await dbContext.LockPostForWriteAsync(postId, cancellationToken);
        if (post is null || (post.Status == PostStatus.Deleted || post.IsHidden))
            return Results.NoContent();

        var like =
            await dbContext.PostLikes
                .SingleOrDefaultAsync(
                    like =>
                        like.PostId == postId
                        && like.UserId ==
                            currentUserId,
                    cancellationToken);

        if (like is null)
        {
            return Results.NoContent();
        }

        dbContext.PostLikes.Remove(
            like);

        /*
         * 当前数据库里仍包含这个 Like，
         * 因为还没 SaveChanges。
         */

        var existingLikeCount =
            await dbContext.PostLikes
                .AsNoTracking()
                .CountAsync(
                    like =>
                        like.PostId == postId,
                    cancellationToken);

        var likeCount =
            Math.Max(
                0,
                existingLikeCount - 1);

        var commentCount =
            await dbContext.PostComments
                .AsNoTracking()
                .CountAsync(
                    comment =>
                        comment.PostId == postId
                        && comment.Status ==
                            PostCommentStatus.Published && !comment.IsHidden && !comment.IsParentHidden,
                    cancellationToken);

        post.RecordMetricsChange();
        await bus.PublishAsync(new RecommendationPreferenceStateChanged(postId, currentUserId, "like", false, post.Revision, DateTimeOffset.UtcNow));
        await bus.PublishAsync(
            new PostMetricsChanged(
                postId,
                likeCount,
                commentCount,
                post.Revision));

        return Results.NoContent();
    }
}
