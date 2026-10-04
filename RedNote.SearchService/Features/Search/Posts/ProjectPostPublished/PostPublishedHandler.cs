using OpenSearch.Client;
using RedNote.Contracts.Content;
using RedNote.SearchService.Domain.Posts;
using RedNote.SearchService.Infrastructure.OpenSearch;

namespace RedNote.SearchService.Features.Search.Posts.ProjectPostPublished;

public static class PostPublishedHandler
{
    public static async Task Handle(
        PostPublished message,
        IOpenSearchClient openSearchClient,
        CancellationToken cancellationToken)
    {
        var document =
            new PostSearchDocument
            {
                Id = message.PostId,
                AuthorUserId = message.AuthorUserId,
                Title = message.Title,
                Content = message.Content,
                Tags = message.Tags,
                LikeCount = message.LikeCount,
                CommentCount = message.CommentCount,
                CreatedAtUtc = message.CreatedAtUtc,
                UpdatedAtUtc = message.UpdatedAtUtc
            };

        var response =
            await openSearchClient.IndexAsync(
                document,
                descriptor => descriptor
                    .Index(
                        OpenSearchIndexInitializer.PostIndexName)
                    .Id(message.PostId),
                cancellationToken);

        if (!response.IsValid)
        {
            throw new InvalidOperationException(
                $"Failed to index post '{message.PostId}'. " +
                response.DebugInformation);
        }
    }
}