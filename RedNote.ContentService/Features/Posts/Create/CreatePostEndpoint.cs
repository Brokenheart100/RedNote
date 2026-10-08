using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using RedNote.Contracts.Media;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Create;

[ApiVersion("1.0")]
[Authorize]
public static class CreatePostEndpoint
{
    public static async Task<(IResult, ValidatedPostInput?)> Before(
        CreatePostRequest request,
        ClaimsPrincipal principal,
        [FromServices] IMediaGrpcService mediaServiceClient,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId))
            return (Results.Unauthorized(), null);

        var mediaIds = request.MediaIds?.Distinct().ToArray() ?? [];
        var mediaResponse = mediaIds.Length == 0 ? new GetMediaBatchResponse() :
            await mediaServiceClient.GetBatchAsync(new GetMediaBatchRequest { MediaIds = [.. mediaIds] }, cancellationToken);

        if (mediaResponse.Items.Count != mediaIds.Length)
            return (InvalidMedia("One or more media items do not exist."), null);

        var mediaById = mediaResponse.Items.ToDictionary(media => media.Id);
        foreach (var mediaId in mediaIds)
        {
            if (!mediaById.TryGetValue(mediaId, out var media))
                return (InvalidMedia($"Media '{mediaId}' does not exist."), null);
            if (media.OwnerUserId != userId)
                return (InvalidMedia("All media items must belong to the current user."), null);
            if (!media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return (InvalidMedia("Only image media can be attached to a post."), null);
        }

        return (WolverineContinue.Result(), new ValidatedPostInput(userId, mediaResponse));
    }

    [WolverinePost("/posts")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status201Created)]
    [Transactional]
    [DeduplicatedWithResponse(DeduplicationScope.User | DeduplicationScope.Endpoint, Required = false)]
    public static async Task<(PostResponse, PostPublished, RecommendationItemStateChanged)> Post(
        CreatePostRequest request,
        ValidatedPostInput input,
        HttpContext httpContext,
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = input.UserId;
        var mediaResponse = input.Media;
        var post = new Domain.Posts.Post(Guid.NewGuid(), userId, request.Title.Trim(), request.Content.Trim());
        var mediaIds = request.MediaIds?.Distinct().ToArray() ?? [];
        var tags = request.Tags is null ? [] : PostTags.Normalize(request.Tags);
        dbContext.Posts.Add(post);
        for (var index = 0; index < mediaIds.Length; index++)
            dbContext.PostMedia.Add(new PostMedia(post.Id, mediaIds[index], index));
        foreach (var tag in tags) dbContext.PostTags.Add(new PostTag(post.Id, tag));

        // Build from the pending write and validated media, before the framework commits.
        // This avoids a second gRPC call after a successful database commit.
        var author = await dbContext.UserProfileProjections.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
        var mediaById = mediaResponse.Items.ToDictionary(media => media.Id);
        var media = mediaIds.Select(id =>
        {
            var item = mediaById[id];
            return new PostMediaResponse(item.Id, item.FileName, item.ContentType, item.Size, item.Url);
        }).ToArray();
        var response = new PostResponse(post.Id, userId,
            new PostAuthorResponse(userId, author?.Nickname, author?.AvatarUrl),
            post.Title, post.Content, mediaIds, media, tags, 0, 0, false, false,
            post.CreatedAtUtc, post.UpdatedAtUtc);

        // Wolverine persists the entity and cascading event in the same EF transaction.
        httpContext.Response.StatusCode = StatusCodes.Status201Created;
        httpContext.Response.Headers.Location = $"/api/v1/posts/{post.Id}";
        return (response,
            new PostPublished(post.Id, userId, post.Title, post.Content, tags, 0, 0,
                post.CreatedAtUtc, post.UpdatedAtUtc, post.Revision), post.RecommendationState(tags));
    }

    private static IResult InvalidMedia(string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["mediaIds"] = [message] });
}

public sealed record ValidatedPostInput(Guid UserId, GetMediaBatchResponse Media);
