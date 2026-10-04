using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetLikedPosts;

[ApiVersion("1.0")]
[Authorize]
public static class GetLikedPostsEndpoint
{
    private const int MaxPageSize = 100;

    [WolverineGet("/posts/liked")]
    public static async Task<IResult> Get(
        int page,
        int pageSize,
        ClaimsPrincipal principal,
        [FromServices] PostResponseQueryService postResponseQueryService,
        [FromServices] ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var subject = principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(subject, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        if (page < 1)
        {
            return ValidationProblem("page", "Page must be greater than or equal to 1.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return ValidationProblem("pageSize", $"PageSize must be between 1 and {MaxPageSize}.");
        }

        var query = dbContext.PostLikes
            .AsNoTracking()
            .Where(like => like.UserId == currentUserId)
            .Join(
                dbContext.Posts.AsNoTracking(),
                like => like.PostId,
                post => post.Id,
                (_, post) => post)
            .Where(post => post.Status == PostStatus.Published);

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

        var items = await postResponseQueryService.BuildAsync(
            posts,
            currentUserId,
            cancellationToken);

        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount,
            items,
        });
    }

    private static IResult ValidationProblem(string key, string message)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [key] = [message],
        });
    }
}