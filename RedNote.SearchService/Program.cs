using OpenSearch.Client;
using OpenSearch.Net;
using RedNote.SearchService.Infrastructure.OpenSearch;
using ServiceDefaults;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var openSearchUrl =
    builder.Configuration["OpenSearch:Url"]
    ?? throw new InvalidOperationException(
        "OpenSearch:Url is not configured.");

builder.Services.AddSingleton<IOpenSearchClient>(
    _ =>
    {
        var node =
            new SingleNodeConnectionPool(
                new Uri(openSearchUrl));

        var settings =
            new ConnectionSettings(node)
                .DefaultIndex(
                    OpenSearchIndexInitializer.PostIndexName);

        return new OpenSearchClient(settings);
    });

builder.Services.AddSingleton<OpenSearchIndexInitializer>();

// PostgreSQL remains the durable inbox store; search documents live in OpenSearch.
var searchDatabaseConnectionString =
    builder.Configuration.GetConnectionString("searchdb")
    ?? throw new InvalidOperationException(
        "Connection string 'searchdb' is not configured.");

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();

    options.PersistMessagesWithPostgresql(
        searchDatabaseConnectionString);


    options.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision()
        .BindExchange("content-events")
        .ToQueue("search-post-events");

    options.ListenToRabbitQueue("search-post-events")
        .UseDurableInbox();
});

builder.Services.AddWolverineHttp();

var app = builder.Build();

await InitializeOpenSearchAsync(app);

app.MapWolverineEndpoints(options =>
{
    options.UseApiVersioning(versioning =>
    {
        versioning.UrlSegmentPrefix = "api/v{version}";
        versioning.UnversionedPolicy = UnversionedPolicy.PassThrough;
    });
});

app.MapDefaultEndpoints();

await app.RunAsync();

static async Task InitializeOpenSearchAsync(
    WebApplication app)
{
    using var scope =
        app.Services.CreateScope();

    var initializer =
        scope.ServiceProvider
            .GetRequiredService<OpenSearchIndexInitializer>();

    await initializer.InitializeAsync(
        app.Lifetime.ApplicationStopping);
}
