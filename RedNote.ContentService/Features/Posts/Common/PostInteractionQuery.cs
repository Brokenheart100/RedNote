using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;

namespace RedNote.ContentService.Features.Posts.Common;


internal static class PostInteractionQuery
{
    public static async Task<PostInteractionResult> LoadAsync(
        ContentServiceDbContext dbContext,
        IReadOnlyCollection<Guid> postIds,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(postIds);

        if (postIds.Count == 0)
        {
            return PostInteractionResult.Empty;
        }

        var likeCounts =
            await dbContext.PostLikes
                .AsNoTracking()
                .Where(
                    like =>
                        postIds.Contains(like.PostId))
                .GroupBy(
                    like =>
                        like.PostId)
                .Select(
                    group => new
                    {
                        PostId = group.Key,
                        Count = group.Count()
                    })
                .ToDictionaryAsync(
                    item =>
                        item.PostId,
                    item =>
                        item.Count,
                    cancellationToken);

        var commentCounts =
            await dbContext.PostComments
                .AsNoTracking()
                .Where(
                    comment =>
                        postIds.Contains(comment.PostId)
                        &&
                        comment.Status ==
                        PostCommentStatus.Published)
                .GroupBy(
                    comment =>
                        comment.PostId)
                .Select(
                    group => new
                    {
                        PostId = group.Key,
                        Count = group.Count()
                    })
                .ToDictionaryAsync(
                    item =>
                        item.PostId,
                    item =>
                        item.Count,
                    cancellationToken);

        HashSet<Guid> likedPostIds = [];
        HashSet<Guid> favoritedPostIds = [];

        if (currentUserId.HasValue)
        {
            var userId =
                currentUserId.Value;

            likedPostIds =
                await dbContext.PostLikes
                    .AsNoTracking()
                    .Where(
                        like =>
                            like.UserId == userId
                            &&
                            postIds.Contains(like.PostId))
                    .Select(
                        like =>
                            like.PostId)
                    .ToHashSetAsync(
                        cancellationToken);

            favoritedPostIds =
                await dbContext.PostFavorites
                    .AsNoTracking()
                    .Where(
                        favorite =>
                            favorite.UserId == userId
                            &&
                            postIds.Contains(
                                favorite.PostId))
                    .Select(
                        favorite =>
                            favorite.PostId)
                    .ToHashSetAsync(
                        cancellationToken);
        }

        return new PostInteractionResult(
            likeCounts,
            commentCounts,
            likedPostIds,
            favoritedPostIds);
    }
}

internal sealed record PostInteractionResult(
    IReadOnlyDictionary<Guid, int> LikeCounts,
    IReadOnlyDictionary<Guid, int> CommentCounts,
    IReadOnlySet<Guid> LikedPostIds,
    IReadOnlySet<Guid> FavoritedPostIds)
{
    public static PostInteractionResult Empty { get; } =
        new(
            new Dictionary<Guid, int>(),
            new Dictionary<Guid, int>(),
            new HashSet<Guid>(),
            new HashSet<Guid>());
}