using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Delete;

[ApiVersion("1.0")]
[Authorize]
public static class DeletePostEndpoint
{
    [WolverineDelete("/posts/{postId:guid}")]
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
                out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var post = await dbContext.LockPostForWriteAsync(postId, cancellationToken);

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
        await bus.PublishAsync(post.RecommendationState([]));

        await bus.PublishAsync(
            new PostDeleted(
                post.Id,
                post.UpdatedAtUtc,
                post.Revision));

        return Results.NoContent();
    }
}
