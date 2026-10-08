using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetPostsByTag;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class GetPostsByTagEndpoint
{
    [WolverineGet("/posts/tags/{tagName}")]
    public static async Task<IResult> Get(
        [AsParameters] TaggedPageQuery paging,
        ClaimsPrincipal principal,
        [FromServices]
        PostResponseQueryService postResponseQueryService,
        [FromServices]
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = (paging.Page, paging.PageSize);
        var normalizedTagName = paging.TagName!.Trim();

        var query =
            dbContext.PostTags
                .AsNoTracking()
                .Where(
                    tag =>
                        EF.Functions.ILike(
                            tag.Name,
                            normalizedTagName))
                .Join(
                    dbContext.Posts
                        .AsNoTracking(),
                    tag =>
                        tag.PostId,
                    post =>
                        post.Id,
                    (tag, post) =>
                        post)
                .Where(
                    post =>
                        post.Status ==
                        PostStatus.Published && !post.IsHidden);

        var totalCount =
            await query.CountAsync(
                cancellationToken);

        var posts =
            await query
                .OrderByDescending(
                    post =>
                        post.CreatedAtUtc)
                .ThenByDescending(
                    post =>
                        post.Id)
                .Skip(
                    (page - 1)
                    * pageSize)
                .Take(
                    pageSize)
                .Select(
                    post =>
                        new PostReadModel(
                            post.Id,
                            post.AuthorUserId,
                            post.Title,
                            post.Content,
                            post.CreatedAtUtc,
                            post.UpdatedAtUtc))
                .ToListAsync(
                    cancellationToken);

        var subject =
            principal.FindFirst(
                "sub")?.Value;

        Guid? currentUserId =
            Guid.TryParse(
                subject,
                out var parsedUserId)
                ? parsedUserId
                : null;

        var items =
            await postResponseQueryService
                .BuildAsync(
                    posts,
                    currentUserId,
                    cancellationToken);

        return Results.Ok(
            new
            {
                page,
                pageSize,
                totalCount,
                items
            });
    }

}