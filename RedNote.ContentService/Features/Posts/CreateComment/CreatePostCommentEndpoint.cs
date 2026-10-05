using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.CreatePostComment;

[ApiVersion("1.0")]
[Authorize]
public static class CreatePostCommentEndpoint
{
    private const int MaxContentLength = 1000;

    [WolverinePost("/posts/{postId:guid}/comments")]
    public static async Task<IResult> Post(
        Guid postId,
        CreatePostCommentRequest request,
        ClaimsPrincipal principal,
        IDbContextOutbox<ContentServiceDbContext> outbox,
        CancellationToken cancellationToken)
    {
        var subject = principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(
                subject,
                out var authorUserId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return ValidationProblem(
                "content",
                "Comment content is required.");
        }

        var content = request.Content.Trim();

        if (content.Length > MaxContentLength)
        {
            return ValidationProblem(
                "content",
                $"Comment content cannot exceed {MaxContentLength} characters.");
        }

        var dbContext = outbox.DbContext;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var post = await PostWriteLock.AcquireAsync(dbContext, postId, cancellationToken);

        if (post is null || post.Status != PostStatus.Published)
        {
            return Results.NotFound();
        }

        if (request.ParentCommentId.HasValue)
        {
            if (request.ParentCommentId.Value == Guid.Empty)
            {
                return ValidationProblem(
                    "parentCommentId",
                    "Parent comment id cannot be empty.");
            }

            var parentComment = await dbContext.PostComments
                .AsNoTracking()
                .Where(
                    comment =>
                        comment.Id == request.ParentCommentId.Value
                        && comment.PostId == postId
                        && comment.Status == PostCommentStatus.Published)
                .Select(
                    comment =>
                        new
                        {
                            comment.Id,
                            comment.ParentCommentId
                        })
                .SingleOrDefaultAsync(cancellationToken);

            if (parentComment is null)
            {
                return ValidationProblem(
                    "parentCommentId",
                    "Parent comment does not exist.");
            }

            if (parentComment.ParentCommentId.HasValue)
            {
                return ValidationProblem(
                    "parentCommentId",
                    "Only one level of replies is supported.");
            }
        }

        var comment = new PostComment(
            Guid.CreateVersion7(),
            postId,
            authorUserId,
            content,
            request.ParentCommentId);

        dbContext.PostComments.Add(comment);

        var likeCount = await dbContext.PostLikes
            .AsNoTracking()
            .CountAsync(
                like => like.PostId == postId,
                cancellationToken);

        /*
         * 此时新 Comment 还只是 Added 状态，
         * 当前数据库 COUNT 不包含它，因此提交后的数量 = existing + 1。
         */
        var existingCommentCount = await dbContext.PostComments
            .AsNoTracking()
            .CountAsync(
                existingComment =>
                    existingComment.PostId == postId
                    && existingComment.Status == PostCommentStatus.Published,
                cancellationToken);

        var commentCount = existingCommentCount + 1;

        post.RecordMetricsChange();
        await outbox.PublishAsync(
            new PostMetricsChanged(
                postId,
                likeCount,
                commentCount,
                post.Revision));

        /*
         * Comment + Wolverine Outbox message 一次提交。
         */
        await outbox.SaveChangesAndFlushMessagesAsync(
            cancellationToken);

        /*
         * 创建后的 HTTP Response 也从本地 Projection 获取作者展示数据。
         *
         * 不调用 UserService，不引入同步跨服务依赖。
         */
        var author = await dbContext.UserProfileProjections
            .AsNoTracking()
            .Where(profile => profile.UserId == authorUserId)
            .Select(
                profile =>
                    new AuthorReadModel(
                        profile.Nickname,
                        profile.AvatarUrl))
            .SingleOrDefaultAsync(cancellationToken);

        return Results.Created(
            $"/api/v1/posts/{postId}/comments/{comment.Id}",
            new PostCommentResponse(
                comment.Id,
                comment.PostId,
                comment.AuthorUserId,
                new CommentAuthorResponse(
                    comment.AuthorUserId,
                    author?.Nickname,
                    author?.AvatarUrl),
                comment.Content,
                comment.ParentCommentId,
                comment.CreatedAtUtc,
                comment.UpdatedAtUtc));
    }

    private static IResult ValidationProblem(
        string key,
        string message)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [key] = [message]
            });
    }

    private sealed record AuthorReadModel(
        string? Nickname,
        string? AvatarUrl);

    private sealed record CommentAuthorResponse(
        Guid UserId,
        string? Nickname,
        string? AvatarUrl);

    private sealed record PostCommentResponse(
        Guid Id,
        Guid PostId,
        Guid AuthorUserId,
        CommentAuthorResponse Author,
        string Content,
        Guid? ParentCommentId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
