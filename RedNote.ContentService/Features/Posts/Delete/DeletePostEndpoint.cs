using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Delete;

[ApiVersion("1.0")]
[Authorize]
public static class DeletePostEndpoint
{
    [WolverineDelete("/posts/{postId:guid}")]
    public static async Task<IResult> Delete(
        Guid postId,
        ClaimsPrincipal principal,
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

        var post =
            await dbContext.Posts
                .SingleOrDefaultAsync(
                    post =>
                        post.Id == postId,
                    cancellationToken);

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

        post.Delete();

        await outbox.PublishAsync(
            new PostDeleted(
                post.Id,
                DateTimeOffset.UtcNow));

        await outbox
            .SaveChangesAndFlushMessagesAsync(
                cancellationToken);

        return Results.NoContent();
    }
}