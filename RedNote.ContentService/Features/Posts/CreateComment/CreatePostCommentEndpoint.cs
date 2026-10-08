using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.CreatePostComment;

[ApiVersion("1.0")]
[Authorize]
public static class CreatePostCommentEndpoint
{
    public static async Task<(IResult, ValidatedCommentInput?)> Before(
        Guid postId,
        CreatePostCommentRequest request,
        ClaimsPrincipal principal,
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var authorUserId))
            return (Results.Unauthorized(), null);

        // The eager transaction must cover the lock and all subsequent writes.
        var post = await dbContext.LockPostForWriteAsync(postId, cancellationToken);
        if (post is null || post.Status != PostStatus.Published || post.IsHidden)
            return (Results.NotFound(), null);

        if (request.ParentCommentId.HasValue)
        {
            var parent = await dbContext.PostComments.AsNoTracking()
                .Where(comment => comment.Id == request.ParentCommentId.Value && comment.PostId == postId
                    && comment.Status == PostCommentStatus.Published && !comment.IsHidden && !comment.IsParentHidden)
                .Select(comment => new { comment.ParentCommentId }).SingleOrDefaultAsync(cancellationToken);
            if (parent is null)
                return (InvalidParent("Parent comment does not exist."), null);
            if (parent.ParentCommentId.HasValue)
                return (InvalidParent("Only one level of replies is supported."), null);
        }

        return (WolverineContinue.Result(), new ValidatedCommentInput(authorUserId, post));
    }

    [WolverinePost("/posts/{postId:guid}/comments")]
    [ProducesResponseType(typeof(PostCommentResponse), StatusCodes.Status201Created)]
    [Transactional]
    [DeduplicatedWithResponse(DeduplicationScope.User | DeduplicationScope.Endpoint, Required = false)]
    public static async Task<(PostCommentResponse, PostMetricsChanged)> Post(
        Guid postId,
        CreatePostCommentRequest request,
        ValidatedCommentInput input,
        HttpContext httpContext,
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authorUserId = input.UserId;
        var post = input.Post;
        var comment = new PostComment(Guid.CreateVersion7(), postId, authorUserId,
            request.Content.Trim(), request.ParentCommentId);
        dbContext.PostComments.Add(comment);
        var likeCount = await dbContext.PostLikes.AsNoTracking().CountAsync(like => like.PostId == postId, cancellationToken);
        // The pending Added comment is not yet included in the database count.
        var commentCount = 1 + await dbContext.PostComments.AsNoTracking()
            .CountAsync(existing => existing.PostId == postId && existing.Status == PostCommentStatus.Published
                && !existing.IsHidden && !existing.IsParentHidden, cancellationToken);
        post.RecordMetricsChange();

        var author = await dbContext.UserProfileProjections.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.UserId == authorUserId, cancellationToken);
        var response = new PostCommentResponse(comment.Id, postId, authorUserId,
            new CommentAuthorResponse(authorUserId, author?.Nickname, author?.AvatarUrl),
            comment.Content, comment.ParentCommentId, comment.CreatedAtUtc, comment.UpdatedAtUtc);
        httpContext.Response.StatusCode = StatusCodes.Status201Created;
        httpContext.Response.Headers.Location = $"/api/v1/posts/{postId}/comments/{comment.Id}";
        return (response,
            new PostMetricsChanged(postId, likeCount, commentCount, post.Revision));
    }

    private static IResult InvalidParent(string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["parentCommentId"] = [message] });
}

public sealed record CommentAuthorResponse(Guid UserId, string? Nickname, string? AvatarUrl);
public sealed record ValidatedCommentInput(Guid UserId, Domain.Posts.Post Post);
public sealed record PostCommentResponse(Guid Id, Guid PostId, Guid AuthorUserId, CommentAuthorResponse Author,
    string Content, Guid? ParentCommentId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
