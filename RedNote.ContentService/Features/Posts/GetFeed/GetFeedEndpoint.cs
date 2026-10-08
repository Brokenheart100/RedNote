using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetFeed;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class GetFeedEndpoint
{

    [WolverineGet("/posts/feed")]
    public static async Task<IResult> Get(
        [AsParameters] PageQuery paging,
        ClaimsPrincipal principal,
        [FromServices] PostResponseQueryService postResponseQueryService,
        [FromServices] ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = (paging.Page, paging.PageSize);
        var query = dbContext.Posts
            .AsNoTracking()
            .Where(post => post.Status == PostStatus.Published && !post.IsHidden);

        var totalCount = await query.CountAsync(cancellationToken);

        var posts = await query
            .OrderByDescending(post => post.CreatedAtUtc)
            .ThenByDescending(post => post.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(post => new PostReadModel(
                post.Id,
                post.AuthorUserId,
                post.Title,
                post.Content,
                post.CreatedAtUtc,
                post.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        Guid? currentUserId = null;

        var subject = principal.FindFirst("sub")?.Value;

        if (Guid.TryParse(subject, out var parsedUserId))
        {
            currentUserId = parsedUserId;
        }

        var items = await postResponseQueryService.BuildAsync(
            posts,
            currentUserId,
            cancellationToken);

        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount,
            items
        });
    }

}