using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Update;

[ApiVersion("1.0")]
[Authorize]
public static class UpdatePostEndpoint
{

    [WolverinePatch("/posts/{postId:guid}")]
    [Transactional]
    public static async Task<IResult> Patch(
        Guid postId,
        UpdatePostRequest request,
        ClaimsPrincipal principal,
        [FromServices]
        PostResponseQueryService postResponseQueryService,
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

        // null preserves tags; an empty list clears them.
        IReadOnlyList<string>? normalizedTags = request.Tags is null ? null : PostTags.Normalize(request.Tags);

        /*
         * Post
         */

        var post = await dbContext.LockPostForWriteAsync(postId, cancellationToken);

        if (
            post is null
            || (post.Status ==
                PostStatus.Deleted || post.IsHidden)
        )
        {
            return Results.NotFound();
        }

        if (post.AuthorUserId !=
            currentUserId)
        {
            return Results.Forbid();
        }

        /*
         * 当前 Tags
         */

        var existingTags =
            await dbContext.PostTags
                .Where(
                    tag =>
                        tag.PostId ==
                        post.Id)
                .ToListAsync(
                    cancellationToken);

        IReadOnlyList<string> finalTags;

        if (normalizedTags is null)
        {
            finalTags =
                existingTags
                    .OrderBy(
                        tag =>
                            tag.Name)
                    .Select(
                        tag =>
                            tag.Name)
                    .ToArray();
        }
        else
        {
            if (existingTags.Count > 0)
            {
                dbContext.PostTags
                    .RemoveRange(
                        existingTags);
            }

            foreach (var tag in normalizedTags)
            {
                dbContext.PostTags.Add(
                    new PostTag(
                        post.Id,
                        tag));
            }

            finalTags =
                normalizedTags
                    .OrderBy(
                        tag =>
                            tag)
                    .ToArray();
        }

        /*
         * 当前 PATCH 不修改点赞和评论，
         * 读取当前统计用于 PostUpdated event。
         */

        var likeCount =
            await dbContext.PostLikes
                .AsNoTracking()
                .CountAsync(
                    like =>
                        like.PostId ==
                        post.Id,
                    cancellationToken);

        var commentCount =
            await dbContext.PostComments
                .AsNoTracking()
                .CountAsync(
                    comment =>
                        comment.PostId ==
                            post.Id
                        && comment.Status ==
                            PostCommentStatus.Published && !comment.IsHidden && !comment.IsParentHidden,
                    cancellationToken);

        /*
         * 修改 Post
         */

        post.Update(
            request.Title.Trim(),
            request.Content.Trim());

        /*
         * Transactional Outbox
         */

        await bus.PublishAsync(
            new PostUpdated(
                post.Id,
                post.AuthorUserId,
                post.Title,
                post.Content,
                finalTags,
                likeCount,
                commentCount,
                post.CreatedAtUtc,
                post.UpdatedAtUtc,
                post.Revision));

        await bus.PublishAsync(post.RecommendationState(finalTags));

        // Flush within the open transaction so response queries see the updated tags.
        // Wolverine commits the transaction and its durable outbox after the endpoint succeeds.
        await dbContext.SaveChangesAsync(cancellationToken);

        /*
         * Response
         */

        var readModel =
            new PostReadModel(
                post.Id,
                post.AuthorUserId,
                post.Title,
                post.Content,
                post.CreatedAtUtc,
                post.UpdatedAtUtc);

        var response =
            await postResponseQueryService
                .BuildSingleAsync(
                    readModel,
                    currentUserId,
                    cancellationToken);

        return Results.Ok(
            response);
    }

}
