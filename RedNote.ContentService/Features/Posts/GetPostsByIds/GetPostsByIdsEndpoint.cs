using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Posts.GetPostsByIds;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class GetPostsByIdsEndpoint
{
    [WolverinePost("/posts/batch")]
    public static async Task<IResult> Post(
        GetPostsByIdsRequest request,
        ClaimsPrincipal principal,
        [FromServices] PostResponseQueryService postResponseQueryService,
        [FromServices] ContentServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var postIds = (request.PostIds ?? [])
            .Where(postId => postId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (postIds.Length == 0)
        {
            return Results.Ok(Array.Empty<PostResponse>());
        }

        var posts = await dbContext.Posts
            .AsNoTracking()
            .Where(post => postIds.Contains(post.Id) && post.Status == PostStatus.Published && !post.IsHidden)
            .Select(post => new PostReadModel(
                post.Id,
                post.AuthorUserId,
                post.Title,
                post.Content,
                post.CreatedAtUtc,
                post.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        if (posts.Count == 0)
        {
            return Results.Ok(Array.Empty<PostResponse>());
        }

        /*
         * EF 的 IN 查询不保证按照输入 ID 顺序返回。
         *
         * SearchService 的顺序代表 OpenSearch 的相关度排序，
         * 所以这里必须恢复调用方传入的顺序。
         */
        var orderById = postIds
            .Select((postId, index) => new { postId, index })
            .ToDictionary(item => item.postId, item => item.index);

        posts.Sort((left, right) => orderById[left.Id].CompareTo(orderById[right.Id]));

        var subject = principal.FindFirst("sub")?.Value;

        Guid? currentUserId = Guid.TryParse(subject, out var parsedUserId)
            ? parsedUserId
            : null;

        var responses = await postResponseQueryService.BuildAsync(
            posts,
            currentUserId,
            cancellationToken);

        return Results.Ok(responses);
    }

    public sealed record GetPostsByIdsRequest(IReadOnlyList<Guid>? PostIds);
}
