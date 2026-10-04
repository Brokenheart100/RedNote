using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Infrastructure.Persistence;

namespace RedNote.ContentService.Features.Posts.Common;


internal static class PostMetadataQuery
{
    public static async Task<PostMetadataResult> LoadAsync(
        ContentServiceDbContext dbContext,
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(postIds);

        if (postIds.Count == 0)
        {
            return PostMetadataResult.Empty;
        }

        var postMedia =
            await dbContext.PostMedia
                .AsNoTracking()
                .Where(
                    item =>
                        postIds.Contains(item.PostId))
                .OrderBy(
                    item =>
                        item.PostId)
                .ThenBy(
                    item =>
                        item.SortOrder)
                .Select(
                    item => new
                    {
                        item.PostId,
                        item.MediaId,
                        item.SortOrder
                    })
                .ToListAsync(
                    cancellationToken);

        var mediaIdsByPost =
            postMedia
                .GroupBy(
                    item =>
                        item.PostId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group
                            .OrderBy(
                                item =>
                                    item.SortOrder)
                            .Select(
                                item =>
                                    item.MediaId)
                            .ToArray());

        var allMediaIds =
            postMedia
                .Select(
                    item =>
                        item.MediaId)
                .Distinct()
                .ToArray();

        var postTags =
            await dbContext.PostTags
                .AsNoTracking()
                .Where(
                    tag =>
                        postIds.Contains(tag.PostId))
                .OrderBy(
                    tag =>
                        tag.PostId)
                .ThenBy(
                    tag =>
                        tag.Name)
                .Select(
                    tag => new
                    {
                        tag.PostId,
                        tag.Name
                    })
                .ToListAsync(
                    cancellationToken);

        var tagsByPost =
            postTags
                .GroupBy(
                    tag =>
                        tag.PostId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group
                            .Select(
                                tag =>
                                    tag.Name)
                            .ToArray());

        return new PostMetadataResult(
            mediaIdsByPost,
            allMediaIds,
            tagsByPost);
    }
}

internal sealed record PostMetadataResult(
    IReadOnlyDictionary<Guid, Guid[]> MediaIdsByPost,
    IReadOnlyList<Guid> AllMediaIds,
    IReadOnlyDictionary<Guid, string[]> TagsByPost)
{
    public static PostMetadataResult Empty { get; } =
        new(
            new Dictionary<Guid, Guid[]>(),
            [],
            new Dictionary<Guid, string[]>());
}