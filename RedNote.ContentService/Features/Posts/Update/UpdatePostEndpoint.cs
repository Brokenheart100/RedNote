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

namespace RedNote.ContentService.Features.Posts.Update;

[ApiVersion("1.0")]
[Authorize]
public static class UpdatePostEndpoint
{
    private const int MaxTagCount = 10;

    [WolverinePatch("/posts/{postId:guid}")]
    public static async Task<IResult> Patch(
        Guid postId,
        UpdatePostRequest request,
        ClaimsPrincipal principal,
        [FromServices]
        PostResponseQueryService postResponseQueryService,
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

        /*
         * Title
         */

        if (string.IsNullOrWhiteSpace(
                request.Title))
        {
            return ValidationProblem(
                "title",
                "Title is required.");
        }

        if (request.Title.Length > 100)
        {
            return ValidationProblem(
                "title",
                "Title cannot exceed 100 characters.");
        }

        /*
         * Content
         */

        if (string.IsNullOrWhiteSpace(
                request.Content))
        {
            return ValidationProblem(
                "content",
                "Content is required.");
        }

        if (request.Content.Length > 5000)
        {
            return ValidationProblem(
                "content",
                "Content cannot exceed 5000 characters.");
        }

        /*
         * Tags
         *
         * null  = 保留
         * []    = 清空
         * [...] = 替换
         */

        IReadOnlyList<string>? normalizedTags =
            null;

        if (request.Tags is not null)
        {
            var tagValidationResult =
                NormalizeTags(
                    request.Tags);

            if (!tagValidationResult.IsValid)
            {
                return ValidationProblem(
                    "tags",
                    tagValidationResult.Error!);
            }

            normalizedTags =
                tagValidationResult.Tags;
        }

        /*
         * Post
         */

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var post = await PostWriteLock.AcquireAsync(dbContext, postId, cancellationToken);

        if (
            post is null
            || post.Status ==
                PostStatus.Deleted
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
                            PostCommentStatus.Published,
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

        await outbox.PublishAsync(
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

        await outbox
            .SaveChangesAndFlushMessagesAsync(
                cancellationToken);

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

    private static TagValidationResult NormalizeTags(
        IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            return new TagValidationResult(
                true,
                [],
                null);
        }

        var normalizedTags =
            new List<string>();

        var tagNames =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var rawTag in tags)
        {
            if (string.IsNullOrWhiteSpace(
                    rawTag))
            {
                return new TagValidationResult(
                    false,
                    [],
                    "Tags cannot contain empty values.");
            }

            var tag =
                rawTag.Trim();

            if (tag.Length > 30)
            {
                return new TagValidationResult(
                    false,
                    [],
                    "Each tag cannot exceed 30 characters.");
            }

            if (tagNames.Add(
                    tag))
            {
                normalizedTags.Add(
                    tag);
            }
        }

        if (normalizedTags.Count >
            MaxTagCount)
        {
            return new TagValidationResult(
                false,
                [],
                $"A post can contain at most {MaxTagCount} tags.");
        }

        return new TagValidationResult(
            true,
            normalizedTags.ToArray(),
            null);
    }

    private static IResult ValidationProblem(
        string key,
        string message)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [key] =
                [
                    message
                ]
            });
    }

    private sealed record TagValidationResult(
        bool IsValid,
        IReadOnlyList<string> Tags,
        string? Error);
}
