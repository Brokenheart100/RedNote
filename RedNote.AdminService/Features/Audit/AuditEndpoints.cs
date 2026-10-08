using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.Authentication;
using RedNote.AdminService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.AdminService.Features.Audit;

[Authorize(Policy = AdminAuthorization.Audit)]
public static class AuditEndpoints
{
    [WolverineGet("/api/v1/admin/content-audit")]
    public static Task<IResult> Content(AdminServiceDbContext db, Guid? actor, Guid? target, string? action,
        DateTimeOffset? from, DateTimeOffset? to, int? page, CancellationToken ct) => Read(db, "content", actor, target, action, from, to, page, ct);

    [WolverineGet("/api/v1/admin/user-audit")]
    public static Task<IResult> Users(AdminServiceDbContext db, Guid? actor, Guid? target, string? action,
        DateTimeOffset? from, DateTimeOffset? to, int? page, CancellationToken ct) => Read(db, "user", actor, target, action, from, to, page, ct);

    [WolverineGet("/api/v1/admin/audit")]
    public static Task<IResult> All(AdminServiceDbContext db, string? source, Guid? actor, Guid? target, string? action,
        DateTimeOffset? from, DateTimeOffset? to, int? page, CancellationToken ct) => Read(db, source, actor, target, action, from, to, page, ct);

    private static async Task<IResult> Read(AdminServiceDbContext db, string? source, Guid? actor, Guid? target,
        string? action, DateTimeOffset? from, DateTimeOffset? to, int? page, CancellationToken ct)
    {
        if (page is <= 0 or > 100000 || (from.HasValue && to.HasValue && from > to)
            || (source is not (null or "content" or "user"))) return Results.BadRequest();
        from = from?.ToUniversalTime();
        to = to?.ToUniversalTime();
        var query = db.Audit.AsNoTracking();
        if (source is not null) query = query.Where(x => x.Source == source);
        if (actor.HasValue) query = query.Where(x => x.ActorUserId == actor);
        if (target.HasValue) query = query.Where(x => x.TargetId == target);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
        if (from.HasValue) query = query.Where(x => x.CreatedAtUtc >= from);
        if (to.HasValue) query = query.Where(x => x.CreatedAtUtc <= to);
        return Results.Ok(new
        {
            total = await query.CountAsync(ct),
            items = await query.OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id).ThenBy(x => x.Source).Skip(((page ?? 1) - 1) * 20).Take(20).ToListAsync(ct)
        });
    }
}
