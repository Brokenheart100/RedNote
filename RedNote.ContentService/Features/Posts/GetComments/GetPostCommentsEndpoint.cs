using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetComments;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class GetPostCommentsEndpoint
{

    [WolverineGet("/posts/{postId:guid}/comments")]
    public static async Task<IResult> Get(
        Guid postId,
        [AsParameters] PageQuery paging,
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = (paging.Page, paging.PageSize);
        var postExists = await dbContext.Posts
            .AsNoTracking()
            .AnyAsync(
                post =>
                    post.Id == postId
                    && post.Status == PostStatus.Published && !post.IsHidden,
                cancellationToken);

        if (!postExists)
        {
            return Results.NotFound();
        }

        var topLevelQuery = dbContext.PostComments
            .AsNoTracking()
            .Where(
                comment =>
                    comment.PostId == postId
                    && comment.ParentCommentId == null
                    && comment.Status == PostCommentStatus.Published && !comment.IsHidden && !comment.IsParentHidden);

        var totalCount = await topLevelQuery
            .CountAsync(cancellationToken);

        /*
         * 顶层评论 + 作者 Projection
         */

        var comments = await (
            from comment in topLevelQuery

            join profile in dbContext.UserProfileProjections.AsNoTracking()
                on comment.AuthorUserId equals profile.UserId into profiles

            from profile in profiles.DefaultIfEmpty()

            orderby
                comment.CreatedAtUtc descending,
                comment.Id descending

            select new CommentReadModel(
                comment.Id,
                comment.PostId,
                comment.AuthorUserId,
                profile == null
                    ? null
                    : profile.Nickname,
                profile == null
                    ? null
                    : profile.AvatarUrl,
                comment.Content,
                comment.ParentCommentId,
                comment.CreatedAtUtc,
                comment.UpdatedAtUtc))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (comments.Count == 0)
        {
            return Results.Ok(
                new PostCommentsResponse(
                    page,
                    pageSize,
                    totalCount,
                    []));
        }

        var commentIds = comments
            .Select(comment => comment.Id)
            .ToArray();

        /*
         * 回复 + 作者 Projection
         */

        var replies = await (
            from reply in dbContext.PostComments.AsNoTracking()

            join profile in dbContext.UserProfileProjections.AsNoTracking()
                on reply.AuthorUserId equals profile.UserId into profiles

            from profile in profiles.DefaultIfEmpty()

            where
                reply.ParentCommentId.HasValue
                && commentIds.Contains(reply.ParentCommentId.Value)
                && reply.Status == PostCommentStatus.Published && !reply.IsHidden && !reply.IsParentHidden

            orderby
                reply.CreatedAtUtc,
                reply.Id

            select new CommentReadModel(
                reply.Id,
                reply.PostId,
                reply.AuthorUserId,
                profile == null
                    ? null
                    : profile.Nickname,
                profile == null
                    ? null
                    : profile.AvatarUrl,
                reply.Content,
                reply.ParentCommentId,
                reply.CreatedAtUtc,
                reply.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var repliesByParentId = replies
            .Where(reply => reply.ParentCommentId.HasValue)
            .GroupBy(reply => reply.ParentCommentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PostCommentResponse>)group
                    .Select(ToResponse)
                    .ToArray());

        var items = comments
            .Select(comment =>
                new PostCommentItemResponse(
                    comment.Id,
                    comment.PostId,
                    comment.AuthorUserId,
                    ToAuthorResponse(comment),
                    comment.Content,
                    comment.ParentCommentId,
                    comment.CreatedAtUtc,
                    comment.UpdatedAtUtc,
                    repliesByParentId.TryGetValue(
                        comment.Id,
                        out var children)
                            ? children
                            : []))
            .ToArray();

        return Results.Ok(
            new PostCommentsResponse(
                page,
                pageSize,
                totalCount,
                items));
    }

    private static PostCommentResponse ToResponse(
        CommentReadModel comment)
    {
        return new PostCommentResponse(
            comment.Id,
            comment.PostId,
            comment.AuthorUserId,
            ToAuthorResponse(comment),
            comment.Content,
            comment.ParentCommentId,
            comment.CreatedAtUtc,
            comment.UpdatedAtUtc);
    }

    private static CommentAuthorResponse ToAuthorResponse(
        CommentReadModel comment)
    {
        return new CommentAuthorResponse(
            comment.AuthorUserId,
            comment.AuthorNickname,
            comment.AuthorAvatarUrl);
    }

    private sealed record CommentReadModel(
        Guid Id,
        Guid PostId,
        Guid AuthorUserId,
        string? AuthorNickname,
        string? AuthorAvatarUrl,
        string Content,
        Guid? ParentCommentId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}