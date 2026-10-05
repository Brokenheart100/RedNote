using OpenSearch.Client;
using RedNote.Contracts.Content;
using RedNote.SearchService.Infrastructure.OpenSearch;

namespace RedNote.SearchService.Features.Search.Posts.ProjectPostDeleted;

public static class PostDeletedHandler
{
    public static Task Handle(PostDeleted message, IOpenSearchClient openSearchClient,
        CancellationToken cancellationToken) => PostProjectionWriter.DeleteAsync(openSearchClient,
            message.PostId, message.Revision, cancellationToken);
}
