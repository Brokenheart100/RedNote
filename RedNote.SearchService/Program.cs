using JasperFx;
using Wolverine.FluentValidation;
using Wolverine.Http.FluentValidation;
using OpenSearch.Client;
using Microsoft.EntityFrameworkCore;
using RedNote.Authentication;
using RedNote.SearchService.Infrastructure.Persistence;
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
            new SingleNodeConnectionPool(new Uri(openSearchUrl));

        var settings =
            new ConnectionSettings(node)
                .DefaultIndex(OpenSearchIndexInitializer.PostIndexName);

        return new OpenSearchClient(settings);
    });

builder.Services.AddSingleton<OpenSearchIndexInitializer>();

// EF owns search history; Wolverine owns its inbox schema in the same database.
var searchDatabaseConnectionString =
    builder.Configuration.GetConnectionString("searchdb")
    ?? throw new InvalidOperationException("Connection string 'searchdb' is not configured.");

builder.Services.AddDbContext<SearchServiceDbContext>(options => options.UseNpgsql(searchDatabaseConnectionString));
builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();
    options.UseFluentValidation();
    options.CodeGeneration.AlwaysUseServiceLocationFor<SearchServiceDbContext>();

    options.PersistMessagesWithPostgresql(searchDatabaseConnectionString);

    options.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision()
        .BindExchange("content-events")
        .ToQueue("search-post-events");

    options.ListenToRabbitQueue("search-post-events")
        .UseDurableInbox();
});

builder.Services.AddWolverineHttp();

var app = builder.Build();
app.UseDefaultExceptionHandler();

if (args.FirstOrDefault() is not ("check-env" or "describe" or "codegen"))
    await InitializeOpenSearchAsync(app);

app.UseAuthentication();
app.UseAuthorization();

app.MapWolverineEndpoints(options =>
{
    options.UseFluentValidationProblemDetailMiddleware();
    options.UseApiVersioning(versioning =>
    {
        versioning.UrlSegmentPrefix = "api/v{version}";
        versioning.UnversionedPolicy = UnversionedPolicy.PassThrough;
    });
});

app.MapDefaultEndpoints();

Environment.ExitCode = await app.RunJasperFxCommands(args);

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
