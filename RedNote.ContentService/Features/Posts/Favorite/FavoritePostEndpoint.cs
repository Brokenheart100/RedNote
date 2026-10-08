using Wolverine;
using Wolverine.Attributes;
using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Favorite;

[ApiVersion("1.0")]
[Authorize]
public static class FavoritePostEndpoint
{
    [WolverinePost("/api/v1/posts/{postId:guid}/favorites")]
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
                out var userId))
        {
            return Results.Unauthorized();
        }

        var post = await dbContext.LockPostForWriteAsync(postId, cancellationToken);
        if (post is null || post.Status != PostStatus.Published || post.IsHidden) return Results.NotFound();

        var alreadyFavorited =
            await dbContext.PostFavorites
                .AsNoTracking()
                .AnyAsync(
                    favorite =>
                        favorite.PostId == postId
                        && favorite.UserId ==
                            userId,
                    cancellationToken);

        if (alreadyFavorited)
        {
            return Results.NoContent();
        }

        dbContext.PostFavorites.Add(
            new PostFavorite(
                postId,
                userId));

        post.RecordInteractionChange();
        await bus.PublishAsync(new RecommendationPreferenceStateChanged(postId, userId, "favorite", true, post.Revision, DateTimeOffset.UtcNow));

        return Results.NoContent();
    }
}