using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alba;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Features.Posts.Favorite;
using RedNote.ContentService.Features.Posts.Unfavorite;
using RedNote.ContentService.Features.Posts.LikePost;
using RedNote.ContentService.Features.Posts.Unlike;
using RedNote.ContentService.Features.Posts.GetPostsByIds;
using RedNote.ContentService.Features.RecommendationSource;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Media;
using RedNote.RecommendationService;
using RedNote.RecommendationService.Features.Backfill;
using RedNote.RecommendationService.Features.Feedback;
using RedNote.RecommendationService.Features.Feed;
using RedNote.RecommendationService.Features.Synchronization;
using RedNote.RecommendationService.Infrastructure.Content;
using RedNote.RecommendationService.Infrastructure.Gorse;
using RedNote.RecommendationService.Infrastructure.Persistence;
using ServiceDefaults;
using StackExchange.Redis;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Http.FluentValidation;
using Wolverine.Postgresql;
using Xunit;

namespace RedNote.Backend.Tests;

[CollectionDefinition("Recommendations")]
public sealed class RecommendationTestGroup : ICollectionFixture<RecommendationFixture>;

public sealed class RecommendationFixture : IAsyncLifetime
{
    public IAlbaHost Host { get; private set; } = null!;
    public IAlbaHost ContentHost { get; private set; } = null!;
    private static string Connection(string name) => new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("REDNOTE_TEST_POSTGRES")) { Database = name }.ConnectionString;
    private readonly string recommendationConnection = Connection("recommendationtest");
    private readonly string contentConnection = Connection("recommendationcontenttest");
    public RecommendationDbContext CreateDb() => new(new DbContextOptionsBuilder<RecommendationDbContext>().UseNpgsql(recommendationConnection).Options);
    public ContentServiceDbContext CreateContentDb() => new(new DbContextOptionsBuilder<ContentServiceDbContext>().UseNpgsql(contentConnection).Options);
    private WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddDefaultProblemDetails(); builder.Services.AddSingleton(this);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, UserHeaderAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddWolverineHttp();
        return builder;
    }
    public async Task InitializeAsync()
    {
        await using (var db = CreateDb()) await db.Database.MigrateAsync();
        await using (var db = CreateContentDb()) await db.Database.MigrateAsync();
        var source = Builder();
        source.Services.AddScoped<PostResponseQueryService>();
        source.Services.AddSingleton<IMediaGrpcService>(new TransactionTestMediaClient());
        source.Host.UseWolverine(o =>
        {
            o.ApplicationAssembly = typeof(FavoritePostEndpoint).Assembly;
            o.UseRuntimeCompilation(); o.UseFluentValidation(); o.Discovery.DisableConventionalDiscovery();
            o.Discovery.IncludeType(typeof(RecommendationSourceHandler)); o.Discovery.IncludeType(typeof(ContentRecommendationTestBridge));
            o.PersistMessagesWithPostgresql(Connection("recommendationcontenttest"));
            o.Services.AddDbContextWithWolverineIntegration<ContentServiceDbContext>(db => db.UseNpgsql(Connection("recommendationcontenttest")));
            o.CodeGeneration.AlwaysUseServiceLocationFor<IMediaGrpcService>();
            o.LocalQueue("source").UseDurableInbox().MaximumParallelMessages(1);
            o.LocalQueue("outbound").UseDurableInbox().MaximumParallelMessages(1);
            o.PublishMessage<ExportRecommendationCatalog>().ToLocalQueue("source");
            o.PublishMessage<ReconcileRecommendationPreferences>().ToLocalQueue("source");
            o.PublishMessage<RecommendationItemStateChanged>().ToLocalQueue("outbound");
            o.PublishMessage<RecommendationPreferenceStateChanged>().ToLocalQueue("outbound");
            o.PublishMessage<RecommendationCatalogExported>().ToLocalQueue("outbound");
            o.PublishMessage<PostMetricsChanged>().ToLocalQueue("outbound");
        });
        ContentHost = await AlbaHost.For(source, app => ConfigureHttp(app, true));
        var builder = Builder();
        builder.AddDefaultHealthChecks();
        builder.Configuration["HealthChecks:Enabled"] = "true";
        builder.Configuration["Recommendations:Endpoint"] = Environment.GetEnvironmentVariable("REDNOTE_TEST_GORSE");
        builder.Configuration["Recommendations:ApiKey"] = "rednote-gorse-test-key";
        builder.Configuration["ConnectionStrings:redis"] = Environment.GetEnvironmentVariable("REDNOTE_TEST_REDIS");
        builder.AddRecommendations();
        builder.Services.AddScoped(_ => new ContentClient(new HttpClient(new RecommendationContentTestTransport(ContentHost.Server.CreateHandler())) { BaseAddress = new Uri("http://localhost/") }));
        builder.Host.UseWolverine(o =>
        {
            o.ApplicationAssembly = typeof(RecommendationEndpoints).Assembly;
            o.UseRuntimeCompilation(); o.UseFluentValidation(); o.Discovery.DisableConventionalDiscovery();
            o.PersistMessagesWithPostgresql(Connection("recommendationtest"));
            o.Services.AddDbContextWithWolverineIntegration<RecommendationDbContext>(db => db.UseNpgsql(Connection("recommendationtest")));
            o.ConfigureRecommendations(useRabbitMq: false); o.Discovery.IncludeType(typeof(RecommendationSourceTestBridge));
            o.LocalQueue("test-inputs").UseDurableInbox().MaximumParallelMessages(1);
            o.LocalQueue("test-source").UseDurableInbox().MaximumParallelMessages(1);
            o.PublishMessage<RecommendationItemStateChanged>().ToLocalQueue("test-inputs");
            o.PublishMessage<RecommendationPreferenceStateChanged>().ToLocalQueue("test-inputs");
            o.PublishMessage<RecommendationCatalogExported>().ToLocalQueue("test-inputs");
            o.PublishMessage<ExportRecommendationCatalog>().ToLocalQueue("test-source");
            o.PublishMessage<ReconcileRecommendationPreferences>().ToLocalQueue("test-source");
        });
        Host = await AlbaHost.For(builder, app => ConfigureHttp(app, false));
    }
    private static void ConfigureHttp(WebApplication app, bool source)
    {
        if (source) app.UseDefaultExceptionHandler(ex => ex is Microsoft.AspNetCore.Http.BadHttpRequestException bad ? bad.StatusCode : 500);
        else app.UseRecommendationExceptionHandler();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapWolverineEndpoints(o =>
        {
            if (source)
            {
                Type[] types = [typeof(GetPostsByIdsEndpoint), typeof(FavoritePostEndpoint), typeof(UnfavoritePostEndpoint), typeof(LikePostEndpoint), typeof(UnlikePostEndpoint)];
                o.CustomizeHttpEndpointDiscovery(q => q.Excludes.WithCondition("content recommendation test", t => !types.Contains(t)));
            }
            o.UseFluentValidationProblemDetailMiddleware(); o.UseApiVersioning(v => v.UrlSegmentPrefix = "api/v{version}");
        });
        if (!source) app.MapDefaultEndpoints();
    }
    public async Task<Guid> Seed(bool publish = true)
    {
        await using var db = CreateContentDb();
        var post = new Post(Guid.NewGuid(), Guid.NewGuid(), "Recommendation integration", "Details belong to ContentService");
        db.Posts.Add(post); db.PostTags.Add(new PostTag(post.Id, "travel")); await db.SaveChangesAsync();
        if (publish) { await Invoke(post.RecommendationState(["travel"])); await Wait(async () => (await Item(post.Id)).StatusCode == HttpStatusCode.OK); }
        return post.Id;
    }
    public async Task Invoke(object message) { using var scope = Host.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(message); }
    public static async Task Forward(IAlbaHost target, object message) { using var scope = target.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message); }
    public async Task<HttpResponseMessage> Send(Guid user, HttpMethod method, string path, object? body = null, bool source = false)
    {
        using var client = (source ? ContentHost : Host).Server.CreateClient();
        using var request = new HttpRequestMessage(method, "/api/v1/posts/" + path);
        request.Headers.Add("X-Test-User", user.ToString()); request.Headers.Add("Authorization", "Bearer " + user);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public static HttpClient GorseHttp() { var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("REDNOTE_TEST_GORSE")!) }; http.DefaultRequestHeaders.Add("X-API-Key", "rednote-gorse-test-key"); return http; }
    public static async Task<HttpResponseMessage> Item(Guid id) { using var http = GorseHttp(); return await http.GetAsync($"api/item/{id}"); }
    public static async Task<JsonElement[]> Feedback(Guid user, Guid post, string type)
    {
        using var http = GorseHttp(); return (await http.GetFromJsonAsync<JsonElement[]>($"api/feedback/{user}/{post}"))!.Where(x => x.GetProperty("FeedbackType").GetString() == type).ToArray();
    }
    public static async Task Wait(Func<Task<bool>> condition) { for (var i = 0; i < 200; i++) { if (await condition()) return; await Task.Delay(100); } Assert.True(await condition(), "Pipeline did not finish within 20 seconds."); }
    public async Task DisposeAsync() { await ContentHost.DisposeAsync(); await Host.DisposeAsync(); }
}
public sealed class RecommendationContentTestTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.Parameter is { } user) request.Headers.Add("X-Test-User", user);
        return base.SendAsync(request, cancellationToken);
    }
}
public static class ContentRecommendationTestBridge
{
    public static Task Handle(RecommendationItemStateChanged m, RecommendationFixture f) => RecommendationFixture.Forward(f.Host, m);
    public static Task Handle(RecommendationPreferenceStateChanged m, RecommendationFixture f) => RecommendationFixture.Forward(f.Host, m);
    public static Task Handle(RecommendationCatalogExported m, RecommendationFixture f) => RecommendationFixture.Forward(f.Host, m);
    public static void Handle(PostMetricsChanged m) { }
}
public static class RecommendationSourceTestBridge
{
    public static Task Handle(ExportRecommendationCatalog m, RecommendationFixture f) => RecommendationFixture.Forward(f.ContentHost, m);
    public static Task Handle(ReconcileRecommendationPreferences m, RecommendationFixture f) => RecommendationFixture.Forward(f.ContentHost, m);
}

[Collection("Recommendations")]
public sealed class RecommendationTests(RecommendationFixture fixture)
{
    [Fact]
    public async Task AspireCachePreservesPrefixExpiryAndReadiness()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var key = $"integration:{Guid.NewGuid():N}";
        try
        {
            await cache.SetStringAsync(key, "shared-connection", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) });
            Assert.Equal("shared-connection", await cache.GetStringAsync(key));
            Assert.True(await redis.KeyExistsAsync("rednote:recommendations:v2:" + key));
            Assert.False(await redis.KeyExistsAsync(key));
            var ttl = await redis.KeyTimeToLiveAsync("rednote:recommendations:v2:" + key);
            Assert.True(ttl is { TotalMinutes: > 29 and <= 30 });
            var health = await scope.ServiceProvider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Contains(health.Entries, entry => entry.Key == "StackExchange.Redis" && entry.Value.Status == HealthStatus.Healthy);
            await fixture.Host.Scenario(s => { s.Get.Url("/health"); s.StatusCodeShouldBeOk(); });
            await fixture.Host.Scenario(s => { s.Get.Url("/alive"); s.StatusCodeShouldBeOk(); });
        }
        finally { await cache.RemoveAsync(key); }
    }
    [Fact]
    public async Task HiddenAndDeletedStateWinsOverOlderEvents()
    {
        var id = await fixture.Seed();
        var state = new RecommendationItemStateChanged(id, Guid.NewGuid(), ["new"], DateTimeOffset.UtcNow, true, true, 3);
        await fixture.Invoke(state); await fixture.Invoke(state with { IsHidden = false, Revision = 2 });
        await RecommendationFixture.Wait(async () => { using var r = await RecommendationFixture.Item(id); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("IsHidden").GetBoolean(); });
        await fixture.Invoke(state with { IsPublished = false, Revision = 4 });
        await fixture.Invoke(state with { IsPublished = true, Revision = 5 });
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Item(id)).StatusCode == HttpStatusCode.NotFound);
        await using var db = fixture.CreateDb(); Assert.False((await db.Items.FindAsync(id))!.IsPublished);
    }
    [Fact]
    public async Task RetriedFeedbackRemainsOneAndCancellationCannotBeReplayedAway()
    {
        var user = Guid.NewGuid(); var id = await fixture.Seed();
        var state = new RecommendationPreferenceStateChanged(id, user, "like", true, 2, DateTimeOffset.UtcNow);
        await fixture.Invoke(state); await fixture.Invoke(state);
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Feedback(user, id, "like")).Length == 1);
        await fixture.Invoke(new ReconcileRecommendationPage());
        await RecommendationFixture.Wait(async () => { await using var db = fixture.CreateDb(); var s = (await db.Feedback.FindAsync(id, user, "like"))!; return s.Version == s.LastAcknowledgedRevision; });
        Assert.Equal(1, (await RecommendationFixture.Feedback(user, id, "like"))[0].GetProperty("Value").GetSingle());
        await fixture.Invoke(state with { IsActive = false, Revision = 3 }); await fixture.Invoke(state);
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Feedback(user, id, "like")).Length == 0);
    }
    [Fact]
    public async Task FavoriteSourceTransactionPublishesVersionsAndCancellation()
    {
        var user = Guid.NewGuid(); var id = await fixture.Seed();
        using var add = await fixture.Send(user, HttpMethod.Post, $"{id}/favorites", source: true); Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Feedback(user, id, "favorite")).Length == 1);
        using var remove = await fixture.Send(user, HttpMethod.Delete, $"{id}/favorites", source: true); Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Feedback(user, id, "favorite")).Length == 0);
        await using var db = fixture.CreateDb(); var state = (await db.Feedback.FindAsync(id, user, "favorite"))!; Assert.False(state.IsActive); Assert.Equal(3, state.SourceRevision);
    }
    [Fact]
    public async Task InitialImportPreservesBrowsingNormalizesValueAndClearsStaleFavorites()
    {
        var user = Guid.NewGuid(); var id = await fixture.Seed(false);
        using var http = RecommendationFixture.GorseHttp();
        await new GorseClient(http).Upsert(new(id.ToString(), false, DateTimeOffset.UtcNow, new { tags = Array.Empty<string>() }), CancellationToken.None);
        using var old = await http.PostAsJsonAsync("api/feedback", new[] { new GorseFeedback("click", user.ToString(), id.ToString(), DateTimeOffset.UtcNow, 7), new GorseFeedback("favorite", user.ToString(), id.ToString(), DateTimeOffset.UtcNow, 4) }); old.EnsureSuccessStatusCode();
        await fixture.Invoke(new InitializeRecommendations());
        await RecommendationFixture.Wait(async () => { await using var db = fixture.CreateDb(); return (await db.Checkpoints.FindAsync("initial-import"))?.Phase == "complete"; });
        await RecommendationFixture.Wait(async () => { var feedback = await RecommendationFixture.Feedback(user, id, "click"); return feedback.Length == 1 && feedback[0].GetProperty("Value").GetSingle() == 1 && (await RecommendationFixture.Feedback(user, id, "favorite")).Length == 0; });
        await using var db = fixture.CreateDb(); var run = (await db.Checkpoints.FindAsync("initial-import"))!.RunId;
        await fixture.Invoke(new InitializeRecommendations());
        await using var next = fixture.CreateDb(); Assert.Equal(run, (await next.Checkpoints.FindAsync("initial-import"))!.RunId);
    }
    [Fact]
    public async Task PagesRefreshContentVisibilityAndRejectCrossUserCursor()
    {
        for (var i = 0; i < 5; i++) await fixture.Seed();
        var user = Guid.NewGuid(); using var first = await fixture.Send(user, HttpMethod.Get, "recommended?pageSize=2"); first.EnsureSuccessStatusCode();
        var page = (await first.Content.ReadFromJsonAsync<RecommendationFeedResponse>())!; Assert.Equal(2, page.Items.Count); Assert.True(page.HasMore);
        using var bad = await fixture.Send(Guid.NewGuid(), HttpMethod.Get, "recommended?cursor=" + Uri.EscapeDataString(page.NextCursor!)); Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        using var scope = fixture.Host.Services.CreateScope(); var snapshot = (await scope.ServiceProvider.GetRequiredService<RecommendationFeed>().GetSnapshot(Guid.Parse(page.RequestId), CancellationToken.None))!;
        var hidden = snapshot.Ids[2]; await using var db = fixture.CreateContentDb(); (await db.Posts.FindAsync(hidden))!.SetHidden(true); await db.SaveChangesAsync();
        using var next = await fixture.Send(user, HttpMethod.Get, "recommended?pageSize=2&cursor=" + Uri.EscapeDataString(page.NextCursor!)); next.EnsureSuccessStatusCode();
        var second = (await next.Content.ReadFromJsonAsync<RecommendationFeedResponse>())!;
        Assert.DoesNotContain(second.Items, x => x.Id == hidden); Assert.DoesNotContain(second.Items, x => page.Items.Any(p => p.Id == x.Id)); Assert.Equal(page.RequestId, second.RequestId);
    }
    [Fact]
    public async Task FeedbackRequiresDeliveryAndDeduplicatesRequests()
    {
        for (var i = 0; i < 3; i++) await fixture.Seed();
        var user = Guid.NewGuid(); using var response = await fixture.Send(user, HttpMethod.Get, "recommended?pageSize=1"); response.EnsureSuccessStatusCode();
        var page = (await response.Content.ReadFromJsonAsync<RecommendationFeedResponse>())!; var post = page.Items[0].Id;
        using var scope = fixture.Host.Services.CreateScope(); var snapshot = (await scope.ServiceProvider.GetRequiredService<RecommendationFeed>().GetSnapshot(Guid.Parse(page.RequestId), CancellationToken.None))!;
        object Body(Guid id, string type = "click") => new { requestId = page.RequestId, items = new[] { new { postId = id, type } } };
        using var badUser = await fixture.Send(Guid.NewGuid(), HttpMethod.Post, "recommendations/feedback", Body(post)); Assert.Equal(HttpStatusCode.Forbidden, badUser.StatusCode);
        using var badType = await fixture.Send(user, HttpMethod.Post, "recommendations/feedback", Body(post, "like")); Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        using var unissued = await fixture.Send(user, HttpMethod.Post, "recommendations/feedback", Body(snapshot.Ids.First(x => x != post))); Assert.Equal(HttpStatusCode.BadRequest, unissued.StatusCode);
        for (var i = 0; i < 3; i++) { using var accepted = await fixture.Send(user, HttpMethod.Post, "recommendations/feedback", Body(post)); Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode); }
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Feedback(user, post, "click")).Length == 1);
        Assert.Equal(1, (await RecommendationFixture.Feedback(user, post, "click"))[0].GetProperty("Value").GetSingle());
        await using var db = fixture.CreateDb(); Assert.Equal(1, await db.Receipts.CountAsync(x => x.RequestId == Guid.Parse(page.RequestId)));
    }
    [Fact]
    public async Task PreferenceBeforeCatalogEventuallySynchronizes()
    {
        var user = Guid.NewGuid(); var id = await fixture.Seed(false); await fixture.Invoke(new RecommendationPreferenceStateChanged(id, user, "favorite", true, 2, DateTimeOffset.UtcNow));
        await using var db = fixture.CreateContentDb(); await fixture.Invoke((await db.Posts.FindAsync(id))!.RecommendationState(["travel"]));
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Feedback(user, id, "favorite")).Length == 1);
    }
    [Fact]
    public async Task ReconciliationRepairsExternalDriftAfterDeletionWasAcknowledged()
    {
        var id = await fixture.Seed(); await fixture.Invoke(new RecommendationItemStateChanged(id, Guid.NewGuid(), [], DateTimeOffset.UtcNow, false, true, 2));
        await RecommendationFixture.Wait(async () => (await RecommendationFixture.Item(id)).StatusCode == HttpStatusCode.NotFound);
        using var http = RecommendationFixture.GorseHttp(); await new GorseClient(http).Upsert(new(id.ToString(), false, DateTimeOffset.UtcNow, new { }), CancellationToken.None);
        await fixture.Invoke(new ReconcileRecommendationPage()); await RecommendationFixture.Wait(async () => (await RecommendationFixture.Item(id)).StatusCode == HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task GorseOutageFallsBackToProjectionAndContentBatch()
    {
        var id = await fixture.Seed(); using var scope = fixture.Host.Services.CreateScope(); using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") };
        var feed = new RecommendationFeed(new GorseClient(http), scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(), scope.ServiceProvider.GetRequiredService<RecommendationDbContext>(), scope.ServiceProvider.GetRequiredService<ContentClient>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<RecommendationFeed>.Instance);
        var page = await feed.Get(Guid.NewGuid(), 50, null, null, CancellationToken.None); Assert.Equal("latest", page.Strategy); Assert.Contains(page.Items, x => x.Id == id);
    }
    [Fact]
    public async Task BatchPreservesDelegatedUserInteractionFlags()
    {
        var user = Guid.NewGuid(); var id = await fixture.Seed(); using var like = await fixture.Send(user, HttpMethod.Post, $"{id}/likes", source: true); like.EnsureSuccessStatusCode();
        using var response = await fixture.Send(user, HttpMethod.Get, "recommended?pageSize=50"); response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<RecommendationFeedResponse>())!.Items.Single(x => x.Id == id).IsLiked);
    }
    [Fact]
    public async Task GorseRejectsMissingKey()
    {
        using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("REDNOTE_TEST_GORSE")!) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("api/items")).StatusCode);
    }
}
