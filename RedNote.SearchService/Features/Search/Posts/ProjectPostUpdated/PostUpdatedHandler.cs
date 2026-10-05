using OpenSearch.Client;
using RedNote.Contracts.Content;
using RedNote.SearchService.Domain.Posts;
using RedNote.SearchService.Infrastructure.OpenSearch;

namespace RedNote.SearchService.Features.Search.Posts.ProjectPostUpdated;


public static class PostUpdatedHandler
{
    public static async Task Handle(
        PostUpdated message,
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

        await PostProjectionWriter.WriteSnapshotAsync(
            openSearchClient, document, message.Revision, cancellationToken);
    }
}
