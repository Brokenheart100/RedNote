using System.Diagnostics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RedNote.Authentication;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Admin;
using RedNote.Contracts.Content;
using Wolverine;

namespace RedNote.ContentService.Features.Admin;

internal static class AdminContentEndpoints
{
    public static void MapAdminContent(this WebApplication app)
    {
        var group = app.MapGroup("/internal/admin");
        group.MapGet("/posts", async (ContentServiceDbContext db, string? q, string? state, int? page, CancellationToken ct) =>
        {
            var query = db.Posts.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(post => post.Title.Contains(q) || post.Content.Contains(q));
            query = state switch
            {
                "published" => query.Where(post => post.Status == PostStatus.Published && !post.IsHidden),
                "hidden" => query.Where(post => post.Status == PostStatus.Published && post.IsHidden),
                "deleted" => query.Where(post => post.Status == PostStatus.Deleted),
                _ => query
            };
            return Results.Ok(new { total = await query.CountAsync(ct), items = await query.OrderByDescending(post => post.CreatedAtUtc)
                .Skip((Math.Clamp(page ?? 1, 1, 100000) - 1) * 20).Take(20).ToListAsync(ct) });
        }).RequireAuthorization(AdminAuthorization.Moderate);
        group.MapGet("/posts/{id:guid}", async (Guid id, ContentServiceDbContext db, CancellationToken ct) =>
            await db.Posts.AsNoTracking().SingleOrDefaultAsync(post => post.Id == id, ct) is { } post ? Results.Ok(post) : Results.NotFound())
            .RequireAuthorization(AdminAuthorization.Moderate);
        group.MapGet("/comments", async (ContentServiceDbContext db, string? q, string? state, int? page, CancellationToken ct) =>
        {
            var query = db.PostComments.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(comment => comment.Content.Contains(q));
            query = state switch
            {
                "published" => query.Where(comment => comment.Status == PostCommentStatus.Published && !comment.IsHidden && !comment.IsParentHidden),
                "hidden" => query.Where(comment => comment.Status == PostCommentStatus.Published && (comment.IsHidden || comment.IsParentHidden)),
                "deleted" => query.Where(comment => comment.Status == PostCommentStatus.Deleted),
                _ => query
            };
            return Results.Ok(new { total = await query.CountAsync(ct), items = await query.OrderByDescending(comment => comment.CreatedAtUtc)
                .Skip((Math.Clamp(page ?? 1, 1, 100000) - 1) * 20).Take(20).ToListAsync(ct) });
        }).RequireAuthorization(AdminAuthorization.Moderate);
    }

    internal static async Task<IResult> ModeratePost(Guid id, string action, ModerationRequest request, ClaimsPrincipal principal,
        ContentServiceDbContext db, IMessageBus bus, CancellationToken ct)
    {
        if (action is not ("hide" or "restore")) return Results.BadRequest(new { message = "Provide hide/restore, a version and a reason of 1–500 characters." });
        var post = await db.LockPostForWriteAsync(id, ct);
        if (post is null) return Results.NotFound();
        if (post.Revision != request.Revision || post.Status == PostStatus.Deleted) return Results.Conflict();
        var hidden = action == "hide";
        if (post.IsHidden == hidden) return Results.NoContent();
        post.SetHidden(hidden);
        var audit = Audit(principal, action, "post", id, request.Reason, $"Hidden={hidden};Revision={post.Revision}");
        db.AdminAudit.Add(audit);
        await bus.PublishAsync(ToEvent(audit));
        await bus.PublishAsync(new PostVisibilityChanged(id, hidden, post.Revision));
        await bus.PublishAsync(post.RecommendationState(await db.PostTags.Where(tag => tag.PostId == id).Select(tag => tag.Name).ToArrayAsync(ct)));
        if (!hidden)
        {
            var tags = await db.PostTags.Where(tag => tag.PostId == id).Select(tag => tag.Name).ToListAsync(ct);
            var likes = await db.PostLikes.CountAsync(like => like.PostId == id, ct);
            var comments = await db.PostComments.CountAsync(comment => comment.PostId == id && comment.Status == PostCommentStatus.Published
                && !comment.IsHidden && !comment.IsParentHidden, ct);
            await bus.PublishAsync(new PostPublished(id, post.AuthorUserId, post.Title, post.Content, tags, likes, comments,
                post.CreatedAtUtc, post.UpdatedAtUtc, post.Revision));
        }
        return Results.NoContent();
    }

    internal static async Task<IResult> ModerateComment(Guid id, string action, ModerationRequest request, ClaimsPrincipal principal,
        ContentServiceDbContext db, IMessageBus bus, CancellationToken ct)
    {
        if (action is not ("hide" or "restore")) return Results.BadRequest();
        var postId = await db.PostComments.Where(comment => comment.Id == id).Select(comment => (Guid?)comment.PostId).SingleOrDefaultAsync(ct);
        if (postId is null) return Results.NotFound();
        var post = await db.LockPostForWriteAsync(postId.Value, ct);
        var comment = await db.PostComments.SingleAsync(comment => comment.Id == id, ct);
        if (post is null || post.Status == PostStatus.Deleted || comment.Status == PostCommentStatus.Deleted || comment.Revision != request.Revision)
            return Results.Conflict();
        var hidden = action == "hide";
        if (comment.IsHidden == hidden) return Results.NoContent();
        if (!hidden && comment.ParentCommentId is { } parentId)
        {
            var parent = await db.PostComments.SingleOrDefaultAsync(item => item.Id == parentId, ct);
            if (parent is null || parent.Status == PostCommentStatus.Deleted) return Results.Conflict();
        }
        comment.SetHidden(hidden);
        if (comment.ParentCommentId is null)
        {
            var replies = await db.PostComments.Where(reply => reply.ParentCommentId == id && reply.Status == PostCommentStatus.Published).ToListAsync(ct);
            foreach (var reply in replies) reply.SetParentHidden(hidden);
        }
        var audit = Audit(principal, action, "comment", id, request.Reason, $"Hidden={hidden};Revision={comment.Revision}");
        db.AdminAudit.Add(audit);
        await bus.PublishAsync(ToEvent(audit));
        await db.SaveChangesAsync(ct);
        post.RecordMetricsChange();
        var count = await db.PostComments.CountAsync(item => item.PostId == post.Id && item.Status == PostCommentStatus.Published
            && !item.IsHidden && !item.IsParentHidden, ct);
        var likes = await db.PostLikes.CountAsync(item => item.PostId == post.Id, ct);
        // Hidden posts keep their search tombstone; metrics must not re-publish them.
        await bus.PublishAsync(new PostMetricsChanged(post.Id, likes, count, post.Revision));
        return Results.NoContent();
    }

    private static AdminAuditRecorded ToEvent(AdminAuditEntry entry) => new(entry.Id, "content", entry.ActorUserId,
        entry.Action, entry.TargetType, entry.TargetId, entry.Reason, entry.Change, entry.TraceId, entry.CreatedAtUtc);

    private static AdminAuditEntry Audit(ClaimsPrincipal principal, string action, string type, Guid id, string reason, string change) => new()
    {
        ActorUserId = Guid.Parse(principal.FindFirst("sub")!.Value), Action = $"{type}.{action}", TargetType = type,
        TargetId = id, Reason = reason.Trim(), Change = change, TraceId = Activity.Current?.TraceId.ToString() ?? ""
    };
}

public sealed record ModerationRequest(long Revision, string Reason);
