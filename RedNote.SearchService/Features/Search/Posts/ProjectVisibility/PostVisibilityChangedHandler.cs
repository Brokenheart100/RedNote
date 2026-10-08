using OpenSearch.Client;
using RedNote.Contracts.Content;
using RedNote.SearchService.Infrastructure.OpenSearch;

namespace RedNote.SearchService.Features.Search.Posts.ProjectVisibility;

public static class PostVisibilityChangedHandler
{
    public static Task Handle(PostVisibilityChanged message, IOpenSearchClient client, CancellationToken cancellationToken) =>
        PostProjectionWriter.WriteVisibilityAsync(client, message.PostId, message.IsHidden, message.Revision, cancellationToken);
}
