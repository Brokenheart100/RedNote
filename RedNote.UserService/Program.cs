using Microsoft.EntityFrameworkCore;
using ProtoBuf.Grpc.Server;
using RedNote.Authentication;
using RedNote.Contracts.Users;
using RedNote.UserService.Infrastructure.Persistence;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Grpc;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("userdb")
    ?? throw new InvalidOperationException("Connection string 'userdb' was not found.");

builder.Services.AddDbContext<UserServiceDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);

builder.Services.AddCodeFirstGrpc();

builder.Services.AddWolverineGrpc(options =>
{
    options.IncludeCodeFirstContract<IUserGrpcService>();
});

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();

    options.CodeGeneration.AlwaysUseServiceLocationFor<UserServiceDbContext>();

    options.PersistMessagesWithPostgresql(connectionString);

    options.UseEntityFrameworkCoreTransactions();

    options.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision();

    options.PublishMessage<UserProfileChanged>()
        .ToRabbitExchange("user-events")
        .UseDurableOutbox();
});

builder.Services.AddWolverineHttp();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.MapWolverineGrpcServices();

app.MapWolverineEndpoints(options =>
{
    options.UseApiVersioning(versioning =>
    {
        versioning.UrlSegmentPrefix = "api/v{version}";
        versioning.UnversionedPolicy = UnversionedPolicy.PassThrough;
    });
});

await app.RunAsync();