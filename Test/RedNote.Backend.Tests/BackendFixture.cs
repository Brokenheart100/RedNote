using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Alba;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Features.Posts.LikePost;
using RedNote.ContentService.Features.Posts.Unlike;
using RedNote.ContentService.Features.Posts.Update;
using RedNote.ContentService.Features.Posts.Delete;
using RedNote.ContentService.Features.Posts.DeleteComment;
using RedNote.Contracts.Media;
using ServiceDefaults;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Http.FluentValidation;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using RedNote.MediaService.Infrastructure.Persistence;
using RedNote.SearchService.Infrastructure.OpenSearch;
using RedNote.SearchService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class BackendFixture : IAsyncLifetime
{
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("REDNOTE_TEST_POSTGRES")
        ?? throw new InvalidOperationException("Run scripts/test-backend.ps1 to provision isolated test infrastructure.");
    public IAlbaHost Host { get; private set; } = null!;
    public IOpenSearchClient Search { get; } = new OpenSearchClient(new ConnectionSettings(
        new Uri(Environment.GetEnvironmentVariable("REDNOTE_TEST_OPENSEARCH")
            ?? throw new InvalidOperationException("REDNOTE_TEST_OPENSEARCH is required.")))
        .DefaultIndex(OpenSearchIndexInitializer.PostIndexName));

    public TransactionTestMediaClient Media { get; } = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Logging:LogLevel:Default"] = "Warning";
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
        builder.Services.AddDefaultProblemDetails();
        builder.Services.AddScoped<PostResponseQueryService>();
        builder.Services.AddSingleton<IMediaGrpcService>(Media);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, UserHeaderAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddWolverineHttp();
        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(LikePostEndpoint).Assembly;
            options.UseRuntimeCompilation();
            options.UseFluentValidation();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(MetricsSink));
            options.PersistMessagesWithPostgresql(ConnectionString);
            options.Services.AddDbContextWithWolverineIntegration<ContentServiceDbContext>(db => db.UseNpgsql(ConnectionString));
            options.CodeGeneration.AlwaysUseServiceLocationFor<IMediaGrpcService>();
            options.LocalQueue("metrics").UseDurableInbox();
            options.PublishMessage<RecommendationPreferenceStateChanged>().ToLocalQueue("metrics");
            options.PublishMessage<PostMetricsChanged>().ToLocalQueue("metrics");
            options.PublishMessage<PostUpdated>().ToLocalQueue("metrics");
            options.PublishMessage<PostDeleted>().ToLocalQueue("metrics");
            options.PublishMessage<RecommendationItemStateChanged>().ToLocalQueue("metrics");
        });
        await using (var contentDb = new ContentServiceDbContext(new DbContextOptionsBuilder<ContentServiceDbContext>()
            .UseNpgsql(ConnectionString).Options))
            await contentDb.Database.MigrateAsync();
        await using var mediaDb = CreateMediaDb();
        await mediaDb.Database.MigrateAsync();
        await using var searchDb = CreateSearchDb();
        await searchDb.Database.MigrateAsync();
        Host = await AlbaHost.For(builder, app =>
        {
            app.UseDefaultExceptionHandler(exception => exception is DbUpdateConcurrencyException ? 409 : 500);
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapWolverineEndpoints(options =>
            {
                Type[] endpoints = [typeof(LikePostEndpoint), typeof(UnlikePostEndpoint), typeof(UpdatePostEndpoint),
                    typeof(DeletePostEndpoint), typeof(DeletePostCommentEndpoint)];
                options.CustomizeHttpEndpointDiscovery(query => query.Excludes.WithCondition("transaction endpoints only",
                    type => !endpoints.Contains(type)));
                options.UseFluentValidationProblemDetailMiddleware();
                options.UseApiVersioning(v => v.UrlSegmentPrefix = "api/v{version}");
            });
        });
        await new OpenSearchIndexInitializer(Search,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenSearchIndexInitializer>.Instance).InitializeAsync();
    }

    public async Task<HttpResponseMessage> Send(Guid user, HttpMethod method, string path, object? body = null)
    {
        using var client = Host.Server.CreateClient();
        using var request = new HttpRequestMessage(method, "/api/v1/posts/" + path);
        request.Headers.Add("X-Test-User", user.ToString());
        if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    public MediaServiceDbContext CreateMediaDb() => new(
        new DbContextOptionsBuilder<MediaServiceDbContext>().UseNpgsql(ConnectionString).Options);

    public SearchServiceDbContext CreateSearchDb() => new(
        new DbContextOptionsBuilder<SearchServiceDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
    }
}

public static class MetricsSink
{
    public static void Handle(RecommendationPreferenceStateChanged message) { }
    public static void Handle(RecommendationItemStateChanged message) { }
    public static System.Collections.Concurrent.ConcurrentQueue<PostMetricsChanged> Messages { get; } = new();
    public static void Handle(PostMetricsChanged message) => Messages.Enqueue(message);
    public static void Handle(PostDeleted message) { }
    public static System.Collections.Concurrent.ConcurrentQueue<PostUpdated> Updates { get; } = new();
    public static void Handle(PostUpdated message) => Updates.Enqueue(message);
}

[CollectionDefinition("Backend")]
public sealed class BackendTestGroup : ICollectionFixture<BackendFixture>;

public sealed class TransactionTestMediaClient : IMediaGrpcService
{
    public System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> Failures { get; } = new();
    public Task<GetMediaBatchResponse> GetBatchAsync(GetMediaBatchRequest request, ProtoBuf.Grpc.CallContext context = default)
    {
        if (request.MediaIds.Any(Failures.ContainsKey))
            throw new InvalidOperationException("Simulated response dependency failure.");
        return Task.FromResult(new GetMediaBatchResponse());
    }
}
