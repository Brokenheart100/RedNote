using Microsoft.EntityFrameworkCore;
using RedNote.Authentication;
using RedNote.ContentService.Features.Posts.Common;
using RedNote.ContentService.Features.Users.ProjectUserProfile;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Content;
using RedNote.Contracts.Media;
using RedNote.Contracts.Users;
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


builder.Services.AddWolverineGrpcClient<IUserGrpcService>(options =>
{
    options.Address = new Uri("https://localhost:7135");
});

builder.Services.AddWolverineGrpcClient<IMediaGrpcService>(options =>
{
    options.Address = new Uri("https://localhost:7207");
});

builder.Services.AddScoped<PostResponseQueryService>();
builder.Services.AddWolverineHttp();

var handlerReport = string.Empty;

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();

    options.Discovery.IncludeAssembly(typeof(UserProfileChangedHandler).Assembly);
    options.ApplicationAssembly = typeof(Program).Assembly;

    options.CodeGeneration.AlwaysUseServiceLocationFor<ContentServiceDbContext>();
    options.CodeGeneration.AlwaysUseServiceLocationFor<IMediaGrpcService>();
    options.CodeGeneration.AlwaysUseServiceLocationFor<IUserGrpcService>();

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

    handlerReport = options.DescribeHandlerMatch(typeof(UserProfileChangedHandler));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.Logger.LogInformation(
        "🐺 UserProfileChangedHandler discovery report:\n{HandlerReport}",
        handlerReport);
}

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