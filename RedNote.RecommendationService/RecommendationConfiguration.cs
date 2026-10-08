using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RedNote.Contracts.Recommendations;
using RedNote.RecommendationService.Features;
using RedNote.RecommendationService.Features.Backfill;
using RedNote.RecommendationService.Features.Feed;
using RedNote.RecommendationService.Features.Projection;
using RedNote.RecommendationService.Features.Synchronization;
using RedNote.RecommendationService.Infrastructure.Content;
using RedNote.RecommendationService.Infrastructure.Gorse;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

namespace RedNote.RecommendationService;

public static class RecommendationConfiguration
{
    public static void AddRecommendations(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.AddOptions<RecommendationOptions>().Bind(configuration.GetSection("Recommendations"))
            .Validate(x => Uri.TryCreate(x.Endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "Recommendations:Endpoint must be HTTP(S).")
            .Validate(x => !string.IsNullOrEmpty(x.ApiKey), "Recommendations:ApiKey is required.").ValidateOnStart();
#pragma warning disable EXTEXP0001
        services.AddHttpClient<GorseClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<RecommendationOptions>>().Value;
            http.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
            http.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
            http.Timeout = TimeSpan.FromSeconds(5);
        }).RemoveAllResilienceHandlers(); // Writes are retried by durable messages using CURRENT state.
        services.AddHttpClient<ContentClient>(http =>
        {
            http.BaseAddress = new Uri("https+http://content-service/");
            http.Timeout = TimeSpan.FromSeconds(5);
        }).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.AddRedisClientBuilder("redis")
            .WithDistributedCache(options => options.InstanceName = "rednote:recommendations:v2:");
        services.AddScoped<RecommendationFeed>();
        services.AddHostedService<RecommendationBootstrap>();
    }
    public static void ConfigureRecommendations(this WolverineOptions options, bool useRabbitMq = true)
    {
        options.CodeGeneration.AlwaysUseServiceLocationFor<GorseClient>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<ContentClient>();
        options.Discovery.IncludeType(typeof(RecommendationProjectionHandler));
        options.Discovery.IncludeType(typeof(RecommendationSynchronizationHandler));
        options.Discovery.IncludeType(typeof(RecommendationBackfillHandler));
        options.LocalQueue("gorse-sync").UseDurableInbox().MaximumParallelMessages(1);
        options.PublishMessage<SyncRecommendationItem>().ToLocalQueue("gorse-sync");
        options.LocalQueue("recommendation-maintenance").UseDurableInbox().MaximumParallelMessages(1);
        options.PublishMessage<InitializeRecommendations>().ToLocalQueue("recommendation-maintenance");
        options.PublishMessage<ImportGorseFeedback>().ToLocalQueue("recommendation-maintenance");
        options.PublishMessage<ReconcileRecommendationPage>().ToLocalQueue("recommendation-maintenance");
        options.Schedules.ScheduleRecurring("recommendation-reconcile", "*/15 * * * *", _ => new ReconcileRecommendationPage());
        if (useRabbitMq)
        {
            options.UseRabbitMqUsingNamedConnection("rabbitmq").AutoProvision()
                .BindExchange("recommendation-inputs").ToQueue("recommendation-inputs")
                .BindExchange("recommendation-source-requests").ToQueue("content-recommendation-source");
            options.ListenToRabbitQueue("recommendation-inputs").UseDurableInbox().MaximumParallelMessages(1);
            options.PublishMessage<ExportRecommendationCatalog>().ToRabbitExchange("recommendation-source-requests").UseDurableOutbox();
            options.PublishMessage<ReconcileRecommendationPreferences>().ToRabbitExchange("recommendation-source-requests").UseDurableOutbox();
        }
        options.OnException<DbUpdateException>().RetryWithCooldown(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        options.OnException<HttpRequestException>().RetryWithCooldown(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
        options.OnException<TaskCanceledException>().RetryWithCooldown(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
    }
}
