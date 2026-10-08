namespace RedNote.AppHost.Extensions;

internal static class RecommendationResourceExtensions
{
    public static IResourceBuilder<ProjectResource> AddRedNoteRecommendations(this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres, IResourceBuilder<RedisResource> redis,
        IResourceBuilder<RabbitMQServerResource> rabbitMq, IResourceBuilder<ProjectResource> contentService,
        IResourceBuilder<PostgresDatabaseResource> recommendationDatabase)
    {
        var database = postgres.AddDatabase("gorsedb");
        var apiKey = builder.AddParameter("gorse-api-key", secret: true);
        var dashboardPassword = builder.AddParameter("gorse-dashboard-password", secret: true);
        var pgEndpoint = postgres.GetEndpoint("tcp");
        // Gorse owns its tables. Only database creation is bootstrapped, using psql.
        var databaseInit = builder.AddContainer("gorse-db-init", "postgres", "18.3")
            .WithEnvironment("PGHOST", pgEndpoint.Property(EndpointProperty.Host))
            .WithEnvironment("PGPORT", pgEndpoint.Property(EndpointProperty.Port))
            .WithEnvironment("PGUSER", postgres.Resource.UserNameReference)
            .WithEnvironment("PGPASSWORD", postgres.Resource.PasswordParameter)
            .WithArgs("sh", "-ec", "printf '%s\\n' \"SELECT 'CREATE DATABASE gorsedb' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'gorsedb');\" '\\gexec' | psql -v ON_ERROR_STOP=1 -d postgres")
            .WaitFor(postgres)
            .PublishAsDockerComposeService((_, service) => service.Restart = "no");
        var gorse = builder.AddDockerfile("gorse", "../infrastructure/gorse")
            .WithHttpEndpoint(targetPort: 8088, name: "http")
            .WithHttpHealthCheck("/api/health/ready")
            .WithEnvironment("PGHOST", pgEndpoint.Property(EndpointProperty.Host))
            .WithEnvironment("PGPORT", pgEndpoint.Property(EndpointProperty.Port))
            .WithEnvironment("PGUSER", postgres.Resource.UserNameReference)
            .WithEnvironment("PGPASSWORD", postgres.Resource.PasswordParameter)
            // The native URI includes password escaping and the actual Redis TLS scheme.
            .WithEnvironment("GORSE_CACHE_STORE", redis.Resource.UriExpression)
            .WithEnvironment("GORSE_SERVER_API_KEY", apiKey)
            .WithEnvironment("GORSE_DASHBOARD_USER_NAME", "admin")
            .WithEnvironment("GORSE_DASHBOARD_PASSWORD", dashboardPassword)
            .WithVolume("gorse-models", "/var/lib/gorse")
            .WaitFor(database).WaitForCompletion(databaseInit).WaitFor(redis);
        var recommendationService = builder.AddProject<Projects.RedNote_RecommendationService>("recommendation-service")
            .WithReference(recommendationDatabase).WaitFor(recommendationDatabase)
            .WithReference(redis).WaitFor(redis)
            .WithReference(rabbitMq).WaitFor(rabbitMq)
            .WithReference(contentService)
            .WithEnvironment("Recommendations__Initialize", "true")
            .WithEnvironment("Recommendations__Endpoint", gorse.GetEndpoint("http"))
            .WithEnvironment("Recommendations__ApiKey", apiKey);
        // Gorse readiness does not gate Content or the recommendation fallback.
        return recommendationService;
    }
}
