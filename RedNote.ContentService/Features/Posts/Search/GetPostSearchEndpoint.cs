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
        string q,
        int page,
        int pageSize,
        ClaimsPrincipal principal,
        [FromServices]
        PostResponseQueryService postResponseQueryService,
        [FromServices]
        ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                q))
        {
            return ValidationProblem(
                "q",
                "Search query is required.");
        }

        var keyword =
            q.Trim();

        if (keyword.Length > 100)
        {
            return ValidationProblem(
                "q",
                "Search query cannot exceed 100 characters.");
        }

        if (page < 1)
        {
            return ValidationProblem(
                "page",
                "Page must be greater than or equal to 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            return ValidationProblem(
                "pageSize",
                "PageSize must be between 1 and 100.");
        }

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
                            PostStatus.Published
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

    private static IResult ValidationProblem(
        string key,
        string message)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [key] =
                [
                    message
                ]
            });
    }
}