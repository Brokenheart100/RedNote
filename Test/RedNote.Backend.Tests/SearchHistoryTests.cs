using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RedNote.SearchService.Features.Search.History;
using RedNote.SearchService.Infrastructure.Persistence;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class SearchHistoryTests(BackendFixture fixture)
{
    private static ClaimsPrincipal User(Guid id) => new(new ClaimsIdentity([new Claim("sub", id.ToString())], "test"));

    [Fact]
    public async Task DuplicateKeywordsAreUpdatedAndHistoryIsPrivate()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await using var db = fixture.CreateSearchDb();
        await SearchHistoryEndpoint.Post(new(" Nuxt "), User(owner), db, default);
        await SearchHistoryEndpoint.Post(new("nuxt"), User(owner), db, default);
        await SearchHistoryEndpoint.Post(new("Nuxt"), User(stranger), db, default);
        var entry = await db.SearchHistory.AsNoTracking().SingleAsync(item => item.UserId == owner);
        Assert.Equal("nuxt", entry.Keyword);
        Assert.Equal(200, ((IStatusCodeHttpResult)await SearchHistoryEndpoint.Get(User(owner), db, default)).StatusCode);
        await SearchHistoryEndpoint.Delete(entry.Id, User(stranger), db, default);
        Assert.True(await db.SearchHistory.AnyAsync(item => item.Id == entry.Id));
        await SearchHistoryEndpoint.Clear(User(owner), db, default);
        Assert.False(await db.SearchHistory.AnyAsync(item => item.UserId == owner));
        Assert.True(await db.SearchHistory.AnyAsync(item => item.UserId == stranger));
    }

    [Fact]
    public async Task ConcurrentRecordsKeepOnlyTwentyLatestKeywords()
    {
        var owner = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async number =>
        {
            await using var db = fixture.CreateSearchDb();
            await SearchHistoryEndpoint.Post(new($"keyword-{number}"), User(owner), db, default);
        }));
        await using var verify = fixture.CreateSearchDb();
        Assert.Equal(20, await verify.SearchHistory.CountAsync(item => item.UserId == owner));
        await SearchHistoryEndpoint.Post(new("newest"), User(owner), verify, default);
        Assert.Equal("newest", (await verify.SearchHistory.AsNoTracking().Where(item => item.UserId == owner)
            .OrderByDescending(item => item.LastSearchedAtUtc).FirstAsync()).Keyword);
        Assert.Equal(20, await verify.SearchHistory.CountAsync(item => item.UserId == owner));
    }

    [Fact]
    public async Task AnonymousRequestsDoNotWriteHistory()
    {
        var owner = Guid.NewGuid();
        await using var db = fixture.CreateSearchDb();
        Assert.Equal(401, ((IStatusCodeHttpResult)await SearchHistoryEndpoint.Post(new("valid"), new(), db, default)).StatusCode);
        Assert.False(await db.SearchHistory.AnyAsync(item => item.UserId == owner));
    }

    [Fact]
    public async Task MigrationCreatesMissingDatabaseAndHistorySchema()
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"search_fresh_{Guid.NewGuid():N}" };
        await using var db = new SearchServiceDbContext(new DbContextOptionsBuilder<SearchServiceDbContext>().UseNpgsql(connection.ConnectionString).Options);
        try
        {
            await db.Database.MigrateAsync();
            await SearchHistoryEndpoint.Post(new("fresh database"), User(Guid.NewGuid()), db, default);
            Assert.Equal(1, await db.SearchHistory.CountAsync());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
