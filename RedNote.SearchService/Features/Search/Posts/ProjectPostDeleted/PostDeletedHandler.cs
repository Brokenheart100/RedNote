using OpenSearch.Client;
using RedNote.Contracts.Content;
using RedNote.SearchService.Domain.Posts;
using RedNote.SearchService.Infrastructure.OpenSearch;

namespace RedNote.SearchService.Features.Search.Posts.ProjectPostDeleted;

public static class PostDeletedHandler
{
    public static async Task Handle(
        PostDeleted message,
        IOpenSearchClient openSearchClient,
        CancellationToken cancellationToken)
    {
        var response =
            await openSearchClient.DeleteAsync<PostSearchDocument>(
                message.PostId,
                descriptor => descriptor
                    .Index(
                        OpenSearchIndexInitializer.PostIndexName),
                cancellationToken);

        if (response.IsValid)
        {
            return;
        }

        // RabbitMQ/Wolverine 可能发生重复投递。
        // 文档已经不存在时，删除操作视为成功。
        if (response.ApiCall?.HttpStatusCode == 404)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Failed to delete post '{message.PostId}' " +
            $"from OpenSearch. " +
            response.DebugInformation);
    }
}