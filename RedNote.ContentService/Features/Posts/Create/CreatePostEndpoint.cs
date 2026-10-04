using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using RedNote.Contracts.Media;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Create;

[ApiVersion("1.0")]
[Authorize]
public static class CreatePostEndpoint
{
    private const int MaxMediaCount = 9;
    private const int MaxTagCount = 10;

    [WolverinePost("/posts")]
    public static async Task<IResult> Post(
        CreatePostRequest request,
        ClaimsPrincipal principal,
        [FromServices] IMediaGrpcService mediaServiceClient,
        [FromServices] PostResponseQueryService postResponseQueryService,
        [FromServices] IDbContextOutbox<ContentServiceDbContext> outbox,
        CancellationToken cancellationToken)
    {
        var dbContext = outbox.DbContext;

        var subject = principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(subject, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        /*
         * Title
         */

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return ValidationProblem("title", "Title is required.");
        }

        if (request.Title.Length > 100)
        {
            return ValidationProblem("title", "Title cannot exceed 100 characters.");
        }

        /*
         * Content
         */

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return ValidationProblem("content", "Content is required.");
        }

        if (request.Content.Length > 5000)
        {
            return ValidationProblem("content", "Content cannot exceed 5000 characters.");
        }

        /*
         * MediaIds
         */

        var mediaIds = request.MediaIds?
            .Distinct()
            .ToArray()
            ?? [];

        if (mediaIds.Length > MaxMediaCount)
        {
            return ValidationProblem(
                "mediaIds",
                $"A post can contain at most {MaxMediaCount} media items.");
        }

        if (mediaIds.Any(mediaId => mediaId == Guid.Empty))
        {
            return ValidationProblem(
                "mediaIds",
                "MediaIds cannot contain an empty GUID.");
        }

        /*
         * Tags
         */

        var tagValidationResult = NormalizeTags(request.Tags);

        if (!tagValidationResult.IsValid)
        {
            return ValidationProblem(
                "tags",
                tagValidationResult.Error!);
        }

        var tags = tagValidationResult.Tags;

        /*
         * 验证媒体资源
         */

        if (mediaIds.Length > 0)
        {
            var mediaResponse = await mediaServiceClient.GetBatchAsync(
                new GetMediaBatchRequest
                {
                    MediaIds = [.. mediaIds]
                },
                cancellationToken);

            if (mediaResponse.Items.Count != mediaIds.Length)
            {
                return ValidationProblem(
                    "mediaIds",
                    "One or more media items do not exist.");
            }

            var mediaById = mediaResponse.Items.ToDictionary(media => media.Id);

            foreach (var mediaId in mediaIds)
            {
                if (!mediaById.TryGetValue(mediaId, out var media))
                {
                    return ValidationProblem(
                        "mediaIds",
                        $"Media '{mediaId}' does not exist.");
                }

                if (media.OwnerUserId != currentUserId)
                {
                    return ValidationProblem(
                        "mediaIds",
                        "All media items must belong to the current user.");
                }

                if (!media.ContentType.StartsWith(
                        "image/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationProblem(
                        "mediaIds",
                        "Only image media can be attached to a post.");
                }
            }
        }

        /*
         * Post
         */

        var post = new Domain.Posts.Post(
            Guid.NewGuid(),
            currentUserId,
            request.Title.Trim(),
            request.Content.Trim());

        dbContext.Posts.Add(post);

        /*
         * PostMedia
         */

        for (var index = 0; index < mediaIds.Length; index++)
        {
            dbContext.PostMedia.Add(
                new PostMedia(
                    post.Id,
                    mediaIds[index],
                    index));
        }

        /*
         * PostTags
         */

        foreach (var tag in tags)
        {
            dbContext.PostTags.Add(
                new PostTag(
                    post.Id,
                    tag));
        }

        /*
         * Transactional Outbox
         */

        await outbox.PublishAsync(
            new PostPublished(
                post.Id,
                post.AuthorUserId,
                post.Title,
                post.Content,
                tags,
                0,
                0,
                post.CreatedAtUtc,
                post.UpdatedAtUtc));

        await outbox.SaveChangesAndFlushMessagesAsync(
            cancellationToken);

        /*
         * Response
         */

        var readModel = new PostReadModel(
            post.Id,
            post.AuthorUserId,
            post.Title,
            post.Content,
            post.CreatedAtUtc,
            post.UpdatedAtUtc);

        var response = await postResponseQueryService.BuildSingleAsync(
            readModel,
            currentUserId,
            cancellationToken);

        return Results.Created(
            $"/api/v1/posts/{post.Id}",
            response);
    }

    private static TagValidationResult NormalizeTags(
        IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return new TagValidationResult(
                true,
                [],
                null);
        }

        var normalizedTags = new List<string>();
        var tagNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rawTag in tags)
        {
            if (string.IsNullOrWhiteSpace(rawTag))
            {
                return new TagValidationResult(
                    false,
                    [],
                    "Tags cannot contain empty values.");
            }

            var tag = rawTag.Trim();

            if (tag.Length > 30)
            {
                return new TagValidationResult(
                    false,
                    [],
                    "Each tag cannot exceed 30 characters.");
            }

            if (tagNames.Add(tag))
            {
                normalizedTags.Add(tag);
            }
        }

        if (normalizedTags.Count > MaxTagCount)
        {
            return new TagValidationResult(
                false,
                [],
                $"A post can contain at most {MaxTagCount} tags.");
        }

        return new TagValidationResult(
            true,
            [.. normalizedTags],
            null);
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

    private sealed record TagValidationResult(
        bool IsValid,
        IReadOnlyList<string> Tags,
        string? Error);
}