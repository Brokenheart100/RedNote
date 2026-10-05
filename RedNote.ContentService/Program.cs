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

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("contentdb")
    ?? throw new InvalidOperationException("Connection string 'contentdb' was not found.");

builder.Services.AddDbContext<ContentServiceDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);


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

    options.Discovery.IncludeAssembly(typeof(UserProfileChangedHandler).Assembly);
    options.ApplicationAssembly = typeof(Program).Assembly;

    options.CodeGeneration.AlwaysUseServiceLocationFor<ContentServiceDbContext>();
    options.CodeGeneration.AlwaysUseServiceLocationFor<IMediaGrpcService>();

    options.PersistMessagesWithPostgresql(connectionString);
    options.UseEntityFrameworkCoreTransactions();

    options.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision()
        .BindExchange("user-events")
        .ToQueue("content-user-profile-events");

    options.ListenToRabbitQueue("content-user-profile-events")
        .UseDurableInbox();

    options.PublishMessage<PostPublished>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();

    options.PublishMessage<PostUpdated>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();

    options.PublishMessage<PostDeleted>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();

    options.PublishMessage<PostMetricsChanged>()
        .ToRabbitExchange("content-events")
        .UseDurableOutbox();

});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

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
