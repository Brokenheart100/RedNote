using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.Search;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class GetPostSearchEndpoint
{
    [WolverineGet("/posts/search")]
    public static async Task<IResult> Get(
        [AsParameters] PostSearchQuery paging,
        ClaimsPrincipal principal,
        [FromServices]
        PostResponseQueryService postResponseQueryService,
        [FromServices]
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = (paging.Page, paging.PageSize);
        var keyword = paging.Q!.Trim();

        /*
         * LIKE escape
         */

        var escapedKeyword =
            EscapeLikePattern(
                keyword);

        var pattern =
            $"%{escapedKeyword}%";

        /*
         * Search
         */

        var query =
            dbContext.Posts
                .AsNoTracking()
                .Where(
                    post =>
                        post.Status ==
                            PostStatus.Published && !post.IsHidden
                        &&
                        (
                            EF.Functions.ILike(
                                post.Title,
                                pattern,
                                "\\")
                            ||
                            EF.Functions.ILike(
                                post.Content,
                                pattern,
                                "\\")
                        ));

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

        /*
         * Current user
         */

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

    private static string EscapeLikePattern(
        string value)
    {
        return value
            .Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "%",
                "\\%",
                StringComparison.Ordinal)
            .Replace(
                "_",
                "\\_",
                StringComparison.Ordinal);
    }

}