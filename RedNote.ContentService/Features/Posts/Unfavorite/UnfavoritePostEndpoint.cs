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
    public static async Task<IResult> Delete(
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

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return Results.NoContent();
    }
}