using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetPost;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class GetPostEndpoint
{
    [WolverineGet("/posts/{postId:guid}")]
    public static async Task<IResult> Get(
        Guid postId,
        ClaimsPrincipal principal,
        [FromServices] PostResponseQueryService postResponseQueryService,
        [FromServices] ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts
            .AsNoTracking()
            .Where(post => post.Id == postId && post.Status == PostStatus.Published && !post.IsHidden)
            .Select(post => new PostReadModel(
                post.Id,
                post.AuthorUserId,
                post.Title,
                post.Content,
                post.CreatedAtUtc,
                post.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        if (post is null)
        {
            return Results.NotFound();
        }

        Guid? currentUserId = null;
        var subject = principal.FindFirst("sub")?.Value;

        if (Guid.TryParse(subject, out var parsedUserId))
        {
            currentUserId = parsedUserId;
        }

        var response = await postResponseQueryService.BuildSingleAsync(
            post,
            currentUserId,
            cancellationToken);

        return Results.Ok(response);
    }
}