using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Alba;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using ProtoBuf.Grpc;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Features.Posts.Create;
using RedNote.ContentService.Features.Users.ProjectUserProfile;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using RedNote.Contracts.Media;
using RedNote.Contracts.Users;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Http.FluentValidation;
using Wolverine.Postgresql;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class ContentIdempotencyTests(BackendFixture fixture) : IAsyncLifetime
{
    private IAlbaHost _host = null!;
    private HttpClient _client = null!;
    private string _connection = null!;

    public async Task InitializeAsync()
    {
        _connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"idempotency_{Guid.NewGuid():N}" }.ConnectionString;
        await using (var db = CreateDb()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddDefaultProblemDetails();
        builder.Services.AddScoped<PostResponseQueryService>();
        builder.Services.AddSingleton<IMediaGrpcService, EmptyMediaClient>();
        builder.Services.AddSingleton<Validation.HandlerCalls>();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, UserHeaderAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddWolverineHttp();
        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(CreatePostEndpoint).Assembly;
            options.UseRuntimeCompilation();
            options.UseFluentValidation();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(PublishedSink));
            options.Discovery.IncludeType(typeof(UserProfileChangedHandler));
            options.PersistMessagesWithPostgresql(_connection);
            options.Services.AddDbContextWithWolverineIntegration<ContentServiceDbContext>(db => db.UseNpgsql(_connection));
            options.Durability.EnableDeduplicatedResponses = true;
            options.PublishMessage<PostPublished>().ToLocalQueue("published");
            options.PublishMessage<PostMetricsChanged>().ToLocalQueue("published");
            options.PublishMessage<UserProfileChanged>().ToLocalQueue("profile").UseDurableInbox();
            options.CodeGeneration.AlwaysUseServiceLocationFor<IMediaGrpcService>();
        });
        _host = await AlbaHost.For(builder, app =>
        {
            app.UseDefaultExceptionHandler(exception => exception is DbUpdateConcurrencyException ? 409 : 500);
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapWolverineEndpoints(options =>
            {
                options.UseFluentValidationProblemDetailMiddleware();
                options.UseApiVersioning(v => { v.UrlSegmentPrefix = "api/v{version}"; v.UnversionedPolicy = UnversionedPolicy.PassThrough; });
            });
        });
        _client = _host.Server.CreateClient();
    }

    [Fact]
    public async Task DuplicatePostAndCommentOnlyWriteOnce()
    {
        var user = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var first = await Post(user, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var post = await first.Content.ReadFromJsonAsync<PostResponse>();
        var replay = await Post(user, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        var path = $"/api/v1/posts/{post!.Id}/comments";
        var comment = await Send(user, key, path, new { content = "comment" });
        var repeatedComment = await Send(user, key, path, new { content = "comment" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repeatedComment.StatusCode);
        Assert.Equal(await comment.Content.ReadAsStringAsync(), await repeatedComment.Content.ReadAsStringAsync());
        Assert.Equal(comment.Headers.Location, repeatedComment.Headers.Location);
        await using var db = CreateDb();
        Assert.Equal(1, await db.Posts.CountAsync());
        Assert.Equal(1, await db.PostComments.CountAsync());
    }

    [Fact]
    public async Task KeysAreIsolatedBetweenUsersAndCommentTargets()
    {
        var user = Guid.NewGuid();
        const string key = "same-client-key";
        var first = await Post(user, key);
        var second = await Post(Guid.NewGuid(), key);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var one = await first.Content.ReadFromJsonAsync<PostResponse>();
        var two = await second.Content.ReadFromJsonAsync<PostResponse>();
        foreach (var id in new[] { one!.Id, two!.Id })
            Assert.Equal(HttpStatusCode.Created, (await Send(user, key, $"/api/v1/posts/{id}/comments", new { content = "hello" })).StatusCode);
        await using var db = CreateDb();
        Assert.Equal(2, await db.PostComments.CountAsync());
    }

    [Fact]
    public async Task RejectedValidationAndBusinessRulesDoNotPoisonKey()
    {
        var user = Guid.NewGuid();
        const string key = "retry-after-failure";
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(user, key, " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(user, key, "/api/v1/posts", new CreatePostRequest("ok", "content", [Guid.NewGuid()], null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(user, key)).StatusCode);
        await using var db = CreateDb();
        Assert.Equal(1, await db.Posts.CountAsync());
    }

    [Fact]
    public async Task ConcurrentRetriesOnlyCommitOnePost()
    {
        var user = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(user, key)));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(responses.Where(response => response.StatusCode == HttpStatusCode.Created)
            .Select(async response => (await response.Content.ReadFromJsonAsync<PostResponse>())!.Id));
        Assert.Single(ids.Distinct());
        Assert.All(responses.Where(response => response.StatusCode != HttpStatusCode.Created), response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        await using var db = CreateDb();
        Assert.Equal(1, await db.Posts.CountAsync());
    }

    [Fact]
    public async Task LegacyClientsCanOmitKey()
    {
        var user = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Post(user, null)).StatusCode);
        await using var db = CreateDb();
        Assert.Equal(1, await db.Posts.CountAsync());
    }

    [Fact]
    public async Task ProjectionHandlerCommitsAndIgnoresOlderEvents()
    {
        var user = Guid.NewGuid();
        var initial = DateTimeOffset.UtcNow;
        using var scope = _host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new UserProfileChanged(user, "initial", null, initial));
        await using var db = CreateDb();
        Assert.Equal("initial", (await db.UserProfileProjections.AsNoTracking().SingleAsync(profile => profile.UserId == user)).Nickname);
        await bus.InvokeAsync(new UserProfileChanged(user, "updated", null, initial.AddSeconds(1)));
        await bus.InvokeAsync(new UserProfileChanged(user, "outdated", null, initial));
        Assert.Equal("updated", (await db.UserProfileProjections.AsNoTracking().SingleAsync(profile => profile.UserId == user)).Nickname);
    }

    [Fact]
    public async Task SameKeyWithChangedBodyIsRejected()
    {
        var user = Guid.NewGuid();
        const string key = "body-fingerprint";
        Assert.Equal(HttpStatusCode.Created, (await Post(user, key)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(user, key, "changed")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(user, key)).StatusCode);
        await using var db = CreateDb();
        Assert.Equal(1, await db.Posts.CountAsync());
    }

    [Fact]
    public async Task FailedCommitRollsBackCommentRevisionAndEventAndReleasesKey()
    {
        var user = Guid.NewGuid();
        var created = await Post(user, null);
        var postId = (await created.Content.ReadFromJsonAsync<PostResponse>())!.Id;
        var path = $"/api/v1/posts/{postId}/comments";
        const string key = "retry-after-commit-failure";
        await using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Posts\" ADD CONSTRAINT test_revision_failure CHECK (\"Revision\" = 1)");
        try
        {
            var failed = await Send(user, key, path, new { content = "rollback comment" });
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.DoesNotContain("test_revision_failure", await failed.Content.ReadAsStringAsync());
            Assert.Equal(0, await db.PostComments.CountAsync());
            Assert.Equal(1, (await db.Posts.AsNoTracking().SingleAsync(post => post.Id == postId)).Revision);
            Assert.DoesNotContain(PublishedSink.Metrics, message => message.PostId == postId);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Posts\" DROP CONSTRAINT test_revision_failure");
        }
        Assert.Equal(HttpStatusCode.Created, (await Send(user, key, path, new { content = "rollback comment" })).StatusCode);
        Assert.Equal(1, await db.PostComments.CountAsync());
        Assert.Equal(2, (await db.Posts.AsNoTracking().SingleAsync(post => post.Id == postId)).Revision);
    }

    [Fact]
    public async Task InvalidParentDoesNotPoisonCommentKey()
    {
        var user = Guid.NewGuid();
        var created = await Post(user, null);
        var postId = (await created.Content.ReadFromJsonAsync<PostResponse>())!.Id;
        var path = $"/api/v1/posts/{postId}/comments";
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(user, "parent-retry", path,
            new { content = "reply", parentCommentId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(user, "parent-retry", path, new { content = "reply" })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentLikesAndCommentsHaveExactCountsAndIncreasingVersions()
    {
        var created = await Post(Guid.NewGuid(), null);
        var postId = (await created.Content.ReadFromJsonAsync<PostResponse>())!.Id;
        var users = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var writes = users.Select(user => Send(user, null, $"/api/v1/posts/{postId}/likes", new { }));
        var comments = users.Select(user => Send(user, Guid.NewGuid().ToString(), $"/api/v1/posts/{postId}/comments", new { content = "concurrent comment" }));
        var responses = await Task.WhenAll(writes.Concat(comments));
        Assert.All(responses, response => Assert.True(response.IsSuccessStatusCode));
        await using var db = CreateDb();
        Assert.Equal(8, await db.PostLikes.CountAsync(like => like.PostId == postId));
        Assert.Equal(8, await db.PostComments.CountAsync(comment => comment.PostId == postId));
        Assert.Equal(17, (await db.Posts.FindAsync(postId))!.Revision);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (PublishedSink.Metrics.Count(message => message.PostId == postId) < 16)
            await Task.Delay(50, timeout.Token);
        var latest = PublishedSink.Metrics.Where(message => message.PostId == postId).MaxBy(message => message.Revision)!;
        Assert.Equal(17, latest.Revision);
        Assert.Equal(8, latest.LikeCount);
        Assert.Equal(8, latest.CommentCount);
    }

    private Task<HttpResponseMessage> Post(Guid user, string? key, string title = "title") =>
        Send(user, key, "/api/v1/posts", new CreatePostRequest(title, "content", null, null));

    private async Task<HttpResponseMessage> Send(Guid user, string? key, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Test-User", user.ToString());
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request);
    }

    private ContentServiceDbContext CreateDb() => new(new DbContextOptionsBuilder<ContentServiceDbContext>().UseNpgsql(_connection).Options);
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_connection is not null) { await using var db = CreateDb(); await db.Database.EnsureDeletedAsync(); }
    }
}

public sealed class EmptyMediaClient : IMediaGrpcService
{
    public Task<GetMediaBatchResponse> GetBatchAsync(GetMediaBatchRequest request, CallContext context = default) => Task.FromResult(new GetMediaBatchResponse());
}

public static class PublishedSink
{
    public static readonly ConcurrentQueue<PostMetricsChanged> Metrics = new();
    public static void Handle(PostPublished message) { }
    public static void Handle(PostMetricsChanged message) => Metrics.Enqueue(message);
}

public sealed class UserHeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Two users may have the same display name; only sub identifies the caller.
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", Request.Headers["X-Test-User"].ToString()), new Claim("name", "same display name")
        ], "test", "sub", "role"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "test")));
    }
}
