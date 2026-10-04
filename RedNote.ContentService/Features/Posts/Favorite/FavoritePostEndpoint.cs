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
    public static async Task<IResult> Post(
        Guid postId,
        ClaimsPrincipal principal,
        [FromServices]
        ContentServiceDbContext dbContext,
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

        var postExists =
            await dbContext.Posts
                .AsNoTracking()
                .AnyAsync(
                    post =>
                        post.Id == postId
                        && post.Status ==
                            PostStatus.Published,
                    cancellationToken);

        if (!postExists)
        {
            return Results.NotFound();
        }

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

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return Results.NoContent();
    }
}