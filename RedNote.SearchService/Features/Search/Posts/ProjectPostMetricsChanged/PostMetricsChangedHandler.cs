using OpenSearch.Client;
using RedNote.Contracts.Content;
using RedNote.SearchService.Domain.Posts;
using RedNote.SearchService.Infrastructure.OpenSearch;

namespace RedNote.SearchService.Features.Search.Posts.ProjectPostMetricsChanged;

public static class PostMetricsChangedHandler
{
    public static async Task Handle(
        PostMetricsChanged message,
        IOpenSearchClient openSearchClient,
        CancellationToken cancellationToken)
    {
        var response =
            await openSearchClient.UpdateAsync<PostSearchDocument, object>(
                message.PostId,
                descriptor => descriptor
                    .Index(
                        OpenSearchIndexInitializer.PostIndexName)
                    .Doc(
                        new
                        {
                            likeCount = message.LikeCount,
                            commentCount = message.CommentCount
                        }),
                cancellationToken);

        if (response.IsValid)
        {
            return;
        }

        /*
         * 如果帖子已经被删除，PostDeleted 可能先一步
         * 把 OpenSearch 文档删掉。
         *
         * 对这种情况不需要无限重试。
         */
        if (response.ApiCall?.HttpStatusCode == 404)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Failed to update metrics for post " +
            $"'{message.PostId}' in OpenSearch. " +
            response.DebugInformation);
    }
}