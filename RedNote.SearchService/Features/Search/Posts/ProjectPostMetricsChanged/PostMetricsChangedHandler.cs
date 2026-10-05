using OpenSearch.Client;
using RedNote.Contracts.Content;
using RedNote.SearchService.Infrastructure.OpenSearch;

namespace RedNote.SearchService.Features.Search.Posts.ProjectPostMetricsChanged;

public static class PostMetricsChangedHandler
{
    public static Task Handle(PostMetricsChanged message, IOpenSearchClient openSearchClient,
        CancellationToken cancellationToken) => PostProjectionWriter.WriteMetricsAsync(openSearchClient,
            message.PostId, message.LikeCount, message.CommentCount, message.Revision, cancellationToken);
}
