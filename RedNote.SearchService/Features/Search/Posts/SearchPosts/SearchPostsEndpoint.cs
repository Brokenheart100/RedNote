using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenSearch.Client;
using RedNote.SearchService.Domain.Posts;
using RedNote.SearchService.Infrastructure.OpenSearch;
using Wolverine.Http;

namespace RedNote.SearchService.Features.Search.Posts.SearchPosts;

[ApiVersion("1.0")]
[AllowAnonymous]
public static class SearchPostsEndpoint
{
    private const int MaxQueryLength = 100;
    private const int MaxPageSize = 100;

    [WolverineGet("/search/posts")]
    public static async Task<IResult> Get(
        string? q,
        int page,
        int pageSize,
        [FromServices] IOpenSearchClient openSearchClient,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return ValidationProblem("q", "Query is required.");
        }

        var query = q.Trim();

        if (query.Length > MaxQueryLength)
        {
            return ValidationProblem("q", $"Query cannot exceed {MaxQueryLength} characters.");
        }

        if (page < 1)
        {
            return ValidationProblem("page", "Page must be greater than or equal to 1.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return ValidationProblem("pageSize", $"PageSize must be between 1 and {MaxPageSize}.");
        }

        var from = (page - 1) * pageSize;

        var searchResponse = await openSearchClient.SearchAsync<PostSearchDocument>(
            descriptor => descriptor
                .Index(OpenSearchIndexInitializer.PostIndexName)
                .From(from)
                .Size(pageSize)
                .TrackTotalHits(true)
                .TrackScores(true)
                .Query(searchQuery => searchQuery
                    .Bool(booleanQuery => booleanQuery
                        .MustNot(excluded => excluded.Term(term => term
                            .Field(document => document.IsDeleted).Value(true)))
                        .Filter(filter => filter.Exists(exists => exists.Field(document => document.Title)))
                        .Should(
                            should => should
                                .MultiMatch(multiMatch => multiMatch
                                    .Query(query)
                                    .Fields(fields => fields
                                        .Field(document => document.Title, 2.0)
                                        .Field(document => document.Content))),
                            should => should
                                .Term(term => term
                                    .Field(document => document.Tags)
                                    .Value(query)))
                        .MinimumShouldMatch(1)))
                .Sort(sort => sort
                    .Descending(SortSpecialField.Score)
                    .Descending(document => document.CreatedAtUtc)
                    .Descending(document => document.Id)),
            cancellationToken);

        if (!searchResponse.IsValid)
        {
            throw new InvalidOperationException(
                "Failed to search posts in OpenSearch. " +
                searchResponse.DebugInformation);
        }

        /*
         * SearchService 只负责：
         *
         * 1. 哪些帖子匹配
         * 2. 匹配顺序
         * 3. 搜索相关度
         *
         * 完整帖子内容、媒体、作者信息以及当前用户的
         *点赞/收藏状态由 ContentService 负责。
         */
        var items = searchResponse.Hits
            .Select(hit => new SearchPostHitResponse(
                hit.Source.Id,
                hit.Score))
            .ToArray();

        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount = searchResponse.Total,
            items,
        });
    }

    private static IResult ValidationProblem(string key, string message)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [key] = [message],
            });
    }

    private sealed record SearchPostHitResponse(
        Guid PostId,
        double? Score);
}
