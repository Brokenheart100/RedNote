using Wolverine;
using Wolverine.Attributes;
using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Unfavorite;

[ApiVersion("1.0")]
[Authorize]
public static class UnfavoritePostEndpoint
{
    [WolverineDelete("/posts/{postId:guid}/favorites")]
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
                out var userId))
        {
            return Results.Unauthorized();
        }

        var post = await dbContext.LockPostForWriteAsync(postId, cancellationToken);
        if (post is null) return Results.NoContent();

        var favorite =
            await dbContext.PostFavorites
                .SingleOrDefaultAsync(
                    favorite =>
                        favorite.PostId == postId
                        && favorite.UserId == userId,
                    cancellationToken);

        if (favorite is null)
        {
            return Results.NoContent();
        }

        dbContext.PostFavorites.Remove(
            favorite);

        post.RecordInteractionChange();
        await bus.PublishAsync(new RecommendationPreferenceStateChanged(postId, userId, "favorite", false, post.Revision, DateTimeOffset.UtcNow));

        return Results.NoContent();
    }
}