using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.Authentication;
using RedNote.Contracts.Admin;
using RedNote.UserService.Domain.Users;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.UserService.Features.Admin;

internal static class AdminUserEndpoints
{
    public static void MapAdminUsers(this WebApplication app)
    {
        app.MapGet("/internal/admin/users", async (UserServiceDbContext db, string? q, int? page, CancellationToken ct) =>
        {
            var profiles = db.UserProfiles.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q)) profiles = profiles.Where(profile => profile.Nickname != null && profile.Nickname.Contains(q));
            var items = await profiles.OrderByDescending(profile => profile.CreatedAtUtc).Skip((Math.Clamp(page ?? 1, 1, 100000) - 1) * 20).Take(20).ToListAsync(ct);
            var ids = items.Select(item => item.UserId).ToArray();
            var restrictions = await db.UserRestrictions.AsNoTracking().Where(item => ids.Contains(item.UserId)).ToDictionaryAsync(item => item.UserId, ct);
            return Results.Ok(new { total = await profiles.CountAsync(ct), items = items.Select(profile => new { profile.UserId, profile.Nickname,
                profile.AvatarUrl, profile.CreatedAtUtc, restriction = restrictions.GetValueOrDefault(profile.UserId) }) });
        }).RequireAuthorization(AdminAuthorization.Users);

        app.MapGet("/api/v1/users/me/restrictions", async (ClaimsPrincipal principal, UserServiceDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId)) return Results.Unauthorized();
            var item = await db.UserRestrictions.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, ct);
            var active = item is not null && (item.ExpiresAtUtc is null || item.ExpiresAtUtc > DateTimeOffset.UtcNow);
            return Results.Ok(new { publishingRestricted = active && item!.PublishingRestricted, commentingRestricted = active && item!.CommentingRestricted });
        }).RequireAuthorization();

    }
}

public static class UserRestrictionEndpoints
{
    [WolverinePost("/internal/admin/users/{id:guid}/restrictions")]
    [Authorize(Policy = AdminAuthorization.Users)]
    [Transactional, DeduplicatedWithResponse(DeduplicationScope.User | DeduplicationScope.Endpoint)]
    public static async Task<AdminOperationResult> Post(Guid id, RestrictionRequest request, ClaimsPrincipal principal,
        UserServiceDbContext db, IMessageBus bus, CancellationToken ct)
    {
        // Wolverine opens the transaction; lock the profile even for the first restriction write.
        var profile = await db.LockProfileForWriteAsync(id, ct);
        if (profile is null) throw new BadHttpRequestException("User not found.", StatusCodes.Status404NotFound);
        var restriction = await db.UserRestrictions.SingleOrDefaultAsync(item => item.UserId == id, ct);
        if ((restriction?.Revision ?? 0) != request.Revision) throw new BadHttpRequestException("Restriction version changed.", StatusCodes.Status409Conflict);
        if (restriction is null) { restriction = new UserRestriction { UserId = id, Revision = 0 }; db.UserRestrictions.Add(restriction); }
        restriction.PublishingRestricted = request.PublishingRestricted;
        restriction.CommentingRestricted = request.CommentingRestricted;
        restriction.ExpiresAtUtc = request.ExpiresAtUtc?.ToUniversalTime();
        restriction.Revision++;
        restriction.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var audit = new AdminAuditEntry { ActorUserId = Guid.Parse(principal.FindFirst("sub")!.Value), Action = "user.restrictions",
            TargetType = "user", TargetId = id, Reason = request.Reason.Trim(),
            Change = System.Text.Json.JsonSerializer.Serialize(new { request.PublishingRestricted, request.CommentingRestricted, request.ExpiresAtUtc, restriction.Revision }),
            TraceId = Activity.Current?.TraceId.ToString() ?? "" };
        db.AdminAudit.Add(audit);
        await bus.PublishAsync(new AdminAuditRecorded(audit.Id, "user", audit.ActorUserId, audit.Action, audit.TargetType,
            audit.TargetId, audit.Reason, audit.Change, audit.TraceId, audit.CreatedAtUtc));
        return new AdminOperationResult(id, "user.restrictions");
    }
}

public sealed record RestrictionRequest(bool PublishingRestricted, bool CommentingRestricted, DateTimeOffset? ExpiresAtUtc, long Revision, string Reason);
