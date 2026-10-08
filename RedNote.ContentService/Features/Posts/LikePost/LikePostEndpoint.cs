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

namespace RedNote.ContentService.Features.Posts.LikePost;

[ApiVersion("1.0")]
[Authorize]
public static class LikePostEndpoint
{
    [WolverinePost("/posts/{postId:guid}/likes")]
    [Transactional]
    public static async Task<IResult> Post(
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
                            PostCommentStatus.Published && !comment.IsHidden && !comment.IsParentHidden,
                    cancellationToken);

        post.RecordMetricsChange();
        await bus.PublishAsync(new RecommendationPreferenceStateChanged(postId, currentUserId, "like", true, post.Revision, DateTimeOffset.UtcNow));
        await bus.PublishAsync(
            new PostMetricsChanged(
                postId,
                likeCount,
                commentCount,
                post.Revision));

        return Results.NoContent();
    }
}
