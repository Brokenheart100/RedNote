using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetFavorites;

[ApiVersion("1.0")]
[Authorize]
public static class GetMyFavoritePostsEndpoint
{
    [WolverineGet("/posts/favorites")]
    public static async Task<IResult> Get(
        [AsParameters] PageQuery paging,
        ClaimsPrincipal principal,
        [FromServices]
        PostResponseQueryService postResponseQueryService,
        [FromServices]
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = (paging.Page, paging.PageSize);
        var subject =
            principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(
                subject,
                out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var favoritesQuery =
            dbContext.PostFavorites
                .AsNoTracking()
                .Where(
                    favorite =>
                        favorite.UserId ==
                        currentUserId)
                .Join(
                    dbContext.Posts
                        .AsNoTracking(),
                    favorite =>
                        favorite.PostId,
                    post =>
                        post.Id,
                    (favorite, post) =>
                        new
                        {
                            FavoriteCreatedAtUtc =
                                favorite.CreatedAtUtc,

                            Post =
                                post
                        })
                .Where(
                    item =>
                        item.Post.Status ==
                        PostStatus.Published && !item.Post.IsHidden);

        var totalCount =
            await favoritesQuery.CountAsync(
                cancellationToken);

        var posts =
            await favoritesQuery
                .OrderByDescending(
                    item =>
                        item.FavoriteCreatedAtUtc)
                .ThenByDescending(
                    item =>
                        item.Post.Id)
                .Skip(
                    (page - 1)
                    * pageSize)
                .Take(
                    pageSize)
                .Select(
                    item =>
                        new PostReadModel(
                            item.Post.Id,
                            item.Post.AuthorUserId,
                            item.Post.Title,
                            item.Post.Content,
                            item.Post.CreatedAtUtc,
                            item.Post.UpdatedAtUtc))
                .ToListAsync(
                    cancellationToken);

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
