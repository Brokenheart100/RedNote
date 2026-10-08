using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ServiceDiscovery;
using RedNote.Authentication;
using RedNote.RecommendationService;
using RedNote.RecommendationService.Infrastructure.Persistence;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Http.FluentValidation;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
// Local Compose uses HTTP internally; service discovery still prefers HTTPS when available.
builder.Services.Configure<ServiceDiscoveryOptions>(options => options.AllowedSchemes = ["https", "http"]);
builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);
builder.AddRecommendations();
builder.Services.AddWolverineHttp();
var connection = builder.Configuration.GetConnectionString("recommendationdb")
    ?? throw new InvalidOperationException("recommendationdb connection is required.");
builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation(); options.UseFluentValidation();
    options.PersistMessagesWithPostgresql(connection);
    options.Services.AddDbContextWithWolverineIntegration<RecommendationDbContext>(db => db.UseNpgsql(connection));
    options.ConfigureRecommendations();
});
builder.EnrichNpgsqlDbContext<RecommendationDbContext>(settings => settings.DisableRetry = true);
var app = builder.Build();
app.UseRecommendationExceptionHandler();
app.UseAuthentication(); app.UseAuthorization();
app.MapWolverineEndpoints(options =>
{
    options.UseFluentValidationProblemDetailMiddleware();
    options.UseApiVersioning(v => { v.UrlSegmentPrefix = "api/v{version}"; v.UnversionedPolicy = UnversionedPolicy.PassThrough; });
});
app.MapDefaultEndpoints();
Environment.ExitCode = await app.RunJasperFxCommands(args);
