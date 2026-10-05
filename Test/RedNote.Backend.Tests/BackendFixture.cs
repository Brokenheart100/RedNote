using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using RedNote.MediaService.Infrastructure.Persistence;
using RedNote.SearchService.Infrastructure.OpenSearch;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class BackendFixture : IAsyncLifetime
{
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("REDNOTE_TEST_POSTGRES")
        ?? throw new InvalidOperationException("Run scripts/test-backend.ps1 to provision isolated test infrastructure.");
    public IHost Host { get; private set; } = null!;
    public IOpenSearchClient Search { get; } = new OpenSearchClient(new ConnectionSettings(
        new Uri(Environment.GetEnvironmentVariable("REDNOTE_TEST_OPENSEARCH")
            ?? throw new InvalidOperationException("REDNOTE_TEST_OPENSEARCH is required.")))
        .DefaultIndex(OpenSearchIndexInitializer.PostIndexName));

    public async Task InitializeAsync()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Logging:LogLevel:Default"] = "Warning";
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
        builder.Services.AddDbContext<ContentServiceDbContext>(options => options.UseNpgsql(ConnectionString));
        builder.UseWolverine(options =>
        {
            options.UseRuntimeCompilation();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(MetricsSink));
            options.PersistMessagesWithPostgresql(ConnectionString);
            options.UseEntityFrameworkCoreTransactions();
            options.LocalQueue("metrics").UseDurableInbox();
            options.PublishMessage<PostMetricsChanged>().ToLocalQueue("metrics");
        });
        Host = builder.Build();
        using (var scope = Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ContentServiceDbContext>().Database.MigrateAsync();
        await using var mediaDb = CreateMediaDb();
        await mediaDb.Database.MigrateAsync();
        await Host.StartAsync();
        await new OpenSearchIndexInitializer(Search,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenSearchIndexInitializer>.Instance).InitializeAsync();
    }

    public MediaServiceDbContext CreateMediaDb() => new(
        new DbContextOptionsBuilder<MediaServiceDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
    }
}

public static class MetricsSink
{
    public static System.Collections.Concurrent.ConcurrentQueue<PostMetricsChanged> Messages { get; } = new();
    public static void Handle(PostMetricsChanged message) => Messages.Enqueue(message);
    public static void Handle(PostDeleted message) { }
    public static void Handle(PostUpdated message) { }
}

[CollectionDefinition("Backend")]
public sealed class BackendTestGroup : ICollectionFixture<BackendFixture>;
