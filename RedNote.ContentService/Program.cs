using JasperFx;
using Wolverine.FluentValidation;
using Wolverine.Http.FluentValidation;
using Microsoft.EntityFrameworkCore;
using RedNote.Authentication;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Features.Users.ProjectUserProfile;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using RedNote.Contracts.Media;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Grpc.Client;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using RedNote.ContentService.Features.Admin;
using RedNote.Contracts.Admin;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("contentdb")
    ?? throw new InvalidOperationException("Connection string 'contentdb' was not found.");

builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);
builder.Services.AddAdminAuthorization();
builder.Services.AddHttpClient("community-restrictions");


builder.Services.AddWolverineGrpcClient<IMediaGrpcService>(options =>
{
    options.Address = new Uri(builder.Configuration["Grpc:MediaAddress"]
        ?? throw new InvalidOperationException("Grpc:MediaAddress was not configured."));
});

builder.Services.AddScoped<PostResponseQueryService>();
builder.Services.AddWolverineHttp();

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();
    options.UseFluentValidation();
    options.Durability.EnableDeduplicatedResponses = true;

    options.Discovery.IncludeAssembly(typeof(UserProfileChangedHandler).Assembly);
    options.ApplicationAssembly = typeof(Program).Assembly;

    options.CodeGeneration.AlwaysUseServiceLocationFor<IMediaGrpcService>();

    options.PersistMessagesWithPostgresql(connectionString);
    options.Services.AddDbContextWithWolverineIntegration<ContentServiceDbContext>(db => db.UseNpgsql(connectionString));

    options.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision()
        .BindExchange("user-events")
        .ToQueue("content-user-profile-events");

    options.ListenToRabbitQueue("content-user-profile-events")
        .UseDurableInbox();

    options.Discovery.IncludeType(typeof(RecommendationSourceHandler));
    options.UseRabbitMqUsingNamedConnection("rabbitmq").AutoProvision()
        .BindExchange("recommendation-source-requests").ToQueue("content-recommendation-source")
        .BindExchange("recommendation-inputs").ToQueue("recommendation-inputs");
    options.ListenToRabbitQueue("content-recommendation-source").UseDurableInbox().MaximumParallelMessages(1);
    options.PublishMessage<RecommendationItemStateChanged>().ToRabbitExchange("recommendation-inputs").UseDurableOutbox();
    options.PublishMessage<RecommendationPreferenceStateChanged>().ToRabbitExchange("recommendation-inputs").UseDurableOutbox();
    options.PublishMessage<RecommendationCatalogExported>().ToRabbitExchange("recommendation-inputs").UseDurableOutbox();

    options.PublishMessage<PostPublished>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();
    options.PublishMessage<PostVisibilityChanged>().ToRabbitExchange("content-events").UseDurableOutbox();

    options.PublishMessage<PostUpdated>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();

    options.PublishMessage<PostDeleted>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();

    options.PublishMessage<PostMetricsChanged>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();
    options.PublishMessage<AdminAuditRecorded>().ToRabbitExchange("admin-audit-events").UseDurableOutbox();

});

var app = builder.Build();
app.UseDefaultExceptionHandler(exception => exception switch
{
    BadHttpRequestException badRequest => badRequest.StatusCode,
    DbUpdateConcurrencyException => 409,
    _ => 500
});

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CommunityRestrictionMiddleware>();
app.MapAdminContent();

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
