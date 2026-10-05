using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;

namespace RedNote.ContentService.Features.Posts.Common;

internal static class PostWriteLock
{
    // All writes affecting a post's search snapshot must take this lock before
    // reading interactions. The transaction remains open until the outbox commits.
    internal static async Task<Post?> AcquireAsync(
        ContentServiceDbContext dbContext, Guid postId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A post write lock requires an active transaction.");

        var posts = await dbContext.Posts
            .FromSqlInterpolated($"SELECT * FROM \"Posts\" WHERE \"Id\" = {postId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return posts.SingleOrDefault();
    }
}
