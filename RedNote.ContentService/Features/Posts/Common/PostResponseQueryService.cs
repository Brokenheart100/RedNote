using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoBuf.Grpc;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Media;

namespace RedNote.ContentService.Features.Posts.Common;

public sealed class PostResponseQueryService(
    ContentServiceDbContext dbContext,
    IMediaGrpcService mediaServiceClient)
{
    public async Task<PostResponse> BuildSingleAsync(
        PostReadModel post,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var responses = await BuildAsync([post], currentUserId, cancellationToken);

        return responses[0];
    }

    public async Task<IReadOnlyList<PostResponse>> BuildAsync(
        IReadOnlyList<PostReadModel> posts,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var postIds = posts
            .Select(post => post.Id)
            .ToArray();

        /*
         * ContentService 本地元数据
         */

        var metadata = await PostMetadataQuery.LoadAsync(
            dbContext,
            postIds,
            cancellationToken);

        /*
         * 点赞 / 评论 / 收藏状态
         */

        var interactions = await PostInteractionQuery.LoadAsync(
            dbContext,
            postIds,
            currentUserId,
            cancellationToken);

        /*
         * MediaService
         */

        var mediaResponse = await mediaServiceClient.GetBatchAsync(
            new GetMediaBatchRequest
            {
                MediaIds = [.. metadata.AllMediaIds]
            },
            new CallContext(
                new CallOptions(
                    cancellationToken:
                        cancellationToken)));

        var mediaById = mediaResponse.Items
            .ToDictionary(media => media.Id);

        /*
         * ContentService 本地 UserProfile Projection
         */

        var authorIds = posts
            .Select(post => post.AuthorUserId)
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .ToArray();

        var authorsById =
            authorIds.Length == 0
                ? new Dictionary<Guid, UserProfileProjectionReadModel>()
                : await dbContext.UserProfileProjections
                    .AsNoTracking()
                    .Where(profile => authorIds.Contains(profile.UserId))
                    .Select(profile =>
                        new UserProfileProjectionReadModel(
                            profile.UserId,
                            profile.Nickname,
                            profile.AvatarUrl))
                    .ToDictionaryAsync(
                        profile => profile.UserId,
                        cancellationToken);

        /*
         * Response
         */

        var result = new List<PostResponse>(
            posts.Count);

        foreach (var post in posts)
        {
            var mediaIds = metadata.MediaIdsByPost.TryGetValue(
                post.Id,
                out var postMediaIds)
                ? postMediaIds
                : [];

            var media = mediaIds
                .Where(mediaById.ContainsKey)
                .Select(
                    mediaId =>
                    {
                        var item = mediaById[mediaId];

                        return new PostMediaResponse(
                            item.Id,
                            item.FileName,
                            item.ContentType,
                            item.Size,
                            item.Url);
                    })
                .ToArray();

            var tags = metadata.TagsByPost.TryGetValue(
                post.Id,
                out var postTags)
                ? postTags
                : [];

            var likeCount = interactions.LikeCounts.TryGetValue(
                post.Id,
                out var likes)
                ? likes
                : 0;

            var commentCount = interactions.CommentCounts.TryGetValue(
                post.Id,
                out var comments)
                ? comments
                : 0;

            authorsById.TryGetValue(
                post.AuthorUserId,
                out var author);

            var postAuthor = new PostAuthorResponse(
                post.AuthorUserId,
                author?.Nickname,
                author?.AvatarUrl);

            result.Add(
                new PostResponse(
                    post.Id,
                    post.AuthorUserId,
                    postAuthor,
                    post.Title,
                    post.Content,
                    mediaIds,
                    media,
                    tags,
                    likeCount,
                    commentCount,
                    interactions.LikedPostIds.Contains(post.Id),
                    interactions.FavoritedPostIds.Contains(post.Id),
                    post.CreatedAtUtc,
                    post.UpdatedAtUtc));
        }

        return result;
    }

    private sealed record UserProfileProjectionReadModel(
        Guid UserId,
        string? Nickname,
        string? AvatarUrl);
}

public sealed record PostReadModel(
    Guid Id,
    Guid AuthorUserId,
    string Title,
    string Content,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);