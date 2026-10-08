using JasperFx;
using Wolverine.FluentValidation;
using Wolverine.Http.FluentValidation;
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
using RedNote.UserService.Features.Admin;
using RedNote.Contracts.Admin;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("userdb")
    ?? throw new InvalidOperationException("Connection string 'userdb' was not found.");

builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);
builder.Services.AddAdminAuthorization();

builder.Services.AddCodeFirstGrpc();

builder.Services.AddWolverineGrpc(options =>
{
    options.IncludeCodeFirstContract<IUserGrpcService>();
});

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();
    options.UseFluentValidation();
    options.Durability.EnableDeduplicatedResponses = true;

    options.PersistMessagesWithPostgresql(connectionString);

    options.Services.AddDbContextWithWolverineIntegration<UserServiceDbContext>(db => db.UseNpgsql(connectionString));

    options.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision();

    options.PublishMessage<UserProfileChanged>()
        .ToRabbitExchange("user-events")
        .UseDurableOutbox();
    options.PublishMessage<AdminAuditRecorded>().ToRabbitExchange("admin-audit-events").UseDurableOutbox();
});

builder.Services.AddWolverineHttp();

var app = builder.Build();
app.UseDefaultExceptionHandler(exception => exception switch
{
    BadHttpRequestException badRequest => badRequest.StatusCode,
    DbUpdateConcurrencyException => 409,
    UnauthorizedAccessException => 401,
    _ => 500
});

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();
app.MapAdminUsers();

app.MapWolverineGrpcServices();

app.MapWolverineEndpoints(options =>
{
    options.UseFluentValidationProblemDetailMiddleware();
    options.UseApiVersioning(versioning =>
    {
        versioning.UrlSegmentPrefix = "api/v{version}";
        versioning.UnversionedPolicy = UnversionedPolicy.PassThrough;
    });
});

Environment.ExitCode = await app.RunJasperFxCommands(args);
