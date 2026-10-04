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
        string tagName,
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
                tagName))
        {
            return ValidationProblem(
                "tagName",
                "Tag name is required.");
        }

        var normalizedTagName =
            tagName.Trim();

        if (normalizedTagName.Length > 30)
        {
            return ValidationProblem(
                "tagName",
                "Tag name cannot exceed 30 characters.");
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
                        PostStatus.Published);

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