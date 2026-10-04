using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetUserPosts;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class GetUserPostsEndpoint
{
    private const int MaxPageSize = 100;

    [WolverineGet("/posts")]
    public static async Task<IResult> Get(
        Guid authorUserId,
        int page,
        int pageSize,
        ClaimsPrincipal principal,
        [FromServices] PostResponseQueryService postResponseQueryService,
        [FromServices] ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (authorUserId == Guid.Empty)
        {
            return ValidationProblem("authorUserId", "Author user id cannot be empty.");
        }

        if (page < 1)
        {
            return ValidationProblem("page", "Page must be greater than or equal to 1.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return ValidationProblem("pageSize", $"PageSize must be between 1 and {MaxPageSize}.");
        }

        var query = dbContext.Posts
            .AsNoTracking()
            .Where(post => post.AuthorUserId == authorUserId && post.Status == PostStatus.Published);

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

    private static IResult ValidationProblem(string key, string message)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [key] = [message]
            });
    }
}