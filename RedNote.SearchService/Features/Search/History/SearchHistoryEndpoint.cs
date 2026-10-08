using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RedNote.SearchService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.SearchService.Features.Search.History;

[ApiVersion("1.0")]
[Authorize]
public static class SearchHistoryEndpoint
{
    public const int HistoryLimit = 20;

    [WolverineGet("/search/history")]
    public static async Task<IResult> Get(ClaimsPrincipal principal, SearchServiceDbContext db, CancellationToken cancellationToken)
    {
        if (!TryUser(principal, out var userId)) return Results.Unauthorized();
        var items = await db.SearchHistory.AsNoTracking().Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.LastSearchedAtUtc).ThenByDescending(entry => entry.Id)
            .Take(HistoryLimit).Select(entry => new { entry.Id, entry.Keyword, entry.LastSearchedAtUtc })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new { items });
    }

    [WolverinePost("/search/history")]
    public static async Task<IResult> Post(RecordSearchHistoryRequest request, ClaimsPrincipal principal,
        SearchServiceDbContext db, CancellationToken cancellationToken)
    {
        if (!TryUser(principal, out var userId)) return Results.Unauthorized();
        var keyword = request.Keyword!.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize this user's writes so concurrent searches cannot exceed the retention limit.
        await LockUser(db, userId, cancellationToken);
        var id = Guid.CreateVersion7();
        var normalized = keyword.ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO search."SearchHistory" ("Id", "UserId", "Keyword", "NormalizedKeyword", "LastSearchedAtUtc")
            VALUES ({id}, {userId}, {keyword}, {normalized}, {now})
            ON CONFLICT ("UserId", "NormalizedKeyword") DO UPDATE
            SET "Keyword" = EXCLUDED."Keyword", "LastSearchedAtUtc" = EXCLUDED."LastSearchedAtUtc"
            """, cancellationToken);
        var retained = db.SearchHistory.Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.LastSearchedAtUtc).ThenByDescending(entry => entry.Id)
            .Take(HistoryLimit).Select(entry => entry.Id);
        await db.SearchHistory.Where(entry => entry.UserId == userId && !retained.Contains(entry.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    [WolverineDelete("/search/history")]
    public static async Task<IResult> Clear(ClaimsPrincipal principal, SearchServiceDbContext db, CancellationToken cancellationToken)
    {
        if (!TryUser(principal, out var userId)) return Results.Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockUser(db, userId, cancellationToken);
        await db.SearchHistory.Where(entry => entry.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    [WolverineDelete("/search/history/{id:guid}")]
    public static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, SearchServiceDbContext db, CancellationToken cancellationToken)
    {
        if (!TryUser(principal, out var userId)) return Results.Unauthorized();
        await db.SearchHistory.Where(entry => entry.UserId == userId && entry.Id == id).ExecuteDeleteAsync(cancellationToken);
        return Results.NoContent();
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirst("sub")?.Value, out userId) && userId != Guid.Empty;

    private static Task<int> LockUser(SearchServiceDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId.ToString()}, 0))", cancellationToken);
}

public sealed record RecordSearchHistoryRequest(string? Keyword);
