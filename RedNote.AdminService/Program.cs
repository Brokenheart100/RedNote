using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ServiceDiscovery;
using RedNote.Authentication;
using RedNote.AdminService.Features.Audit;
using RedNote.AdminService.Infrastructure.Clients;
using RedNote.AdminService.Infrastructure.Persistence;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
builder.Services.Configure<ServiceDiscoveryOptions>(options => options.AllowedSchemes = ["https", "http"]);
builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);
builder.Services.AddAdminAuthorization();
builder.Services.AddWolverineHttp();
builder.Services.AddScoped<AdminBusinessClient>();
#pragma warning disable EXTEXP0001 // Official removal API: inherited automatic retries are unsafe for forwarded writes.
foreach (var service in new[] { "content-service", "user-service" })
    builder.Services.AddHttpClient(service, client =>
    {
        client.BaseAddress = new Uri($"https+http://{service}");
        client.Timeout = TimeSpan.FromSeconds(10);
    }).RemoveAllResilienceHandlers(); // A timed-out write is ambiguous; never automatically repeat it.
#pragma warning restore EXTEXP0001

var connection = builder.Configuration.GetConnectionString("admindb")
    ?? throw new InvalidOperationException("Connection string 'admindb' was not found.");
builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();
    options.UseFluentValidation();
    options.PersistMessagesWithPostgresql(connection);
    options.Services.AddDbContextWithWolverineIntegration<AdminServiceDbContext>(db => db.UseNpgsql(connection));
    options.UseRabbitMqUsingNamedConnection("rabbitmq").AutoProvision()
        .BindExchange("admin-audit-events").ToQueue("admin-audit-projections");
    options.ListenToRabbitQueue("admin-audit-projections").UseDurableInbox();
});
builder.EnrichNpgsqlDbContext<AdminServiceDbContext>(settings => settings.DisableRetry = true);
var app = builder.Build();
if (builder.Configuration["ImportAuditFile"] is { Length: > 0 } importFile)
{
    using var scope = app.Services.CreateScope();
    var count = await AuditImport.ImportAsync(importFile, scope.ServiceProvider.GetRequiredService<AdminServiceDbContext>());
    app.Logger.LogInformation("Imported {Count} audit records; duplicate IDs were ignored", count);
    return;
}
app.UseDefaultExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapWolverineEndpoints(options => options.UseFluentValidationProblemDetailMiddleware());
app.MapDefaultEndpoints();
Environment.ExitCode = await app.RunJasperFxCommands(args);

public partial class Program;
