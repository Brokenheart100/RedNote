using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

var compose = builder
    .AddDockerComposeEnvironment("compose")
    .ConfigureComposeFile(composeFile =>
    {
        composeFile.Name = "rednote";
        composeFile.Volumes["identity-keys"] = new Volume { Name = "identity-keys" };
        composeFile.Services["postgres"].Healthcheck = new Healthcheck
        {
            Test = ["CMD-SHELL", "pg_isready -U $$POSTGRES_USER"],
            Interval = "5s",
            Timeout = "3s",
            Retries = 20,
            StartPeriod = "10s"
        };
        composeFile.Services["opensearch"].Healthcheck = new Healthcheck
        {
            Test = ["CMD-SHELL", "curl -fsS http://localhost:9200/_cluster/health >/dev/null"],
            Interval = "5s",
            Timeout = "5s",
            Retries = 30,
            StartPeriod = "30s"
        };
        foreach (var service in composeFile.Services.Values)
        {
            if (service.DependsOn.TryGetValue("postgres", out var dependency))
                dependency.Condition = "service_healthy";
            if (service.DependsOn.TryGetValue("opensearch", out var searchDependency))
                searchDependency.Condition = "service_healthy";
            if (!service.Name.EndsWith("-migrations", StringComparison.Ordinal)
                && !service.Name.EndsWith("-init", StringComparison.Ordinal))
                service.Restart = "unless-stopped";
        }
    });

var gatewayPublicUrl = builder.AddParameter(
    "gateway-public-url",
    "http://localhost:8080",
    publishValueAsDefault: true,
    secret: false);

var frontendPublicUrl = builder.AddParameter(
    "frontend-public-url",
    "http://localhost:3000",
    publishValueAsDefault: true,
    secret: false);

var mediaPublicUrl = builder.AddParameter("media-public-url", "http://localhost:9000",
    publishValueAsDefault: true, secret: false);

var nuxtSessionPassword = builder.AddParameter(
    "nuxt-session-password",
    "rednote-dev-auth-utils-session-password-12345678901234567890",
    publishValueAsDefault: true,
    secret: false);

var minioAccessKey = builder.AddParameter(
    "minio-access-key",
    "minioadmin",
    publishValueAsDefault: true,
    secret: false);

var minioSecretKey = builder.AddParameter(
    "minio-secret-key",
    "minioadmin123",
    publishValueAsDefault: true,
    secret: false);

const string jwtAudience = "rednote-api";

var rabbitMq = builder
    .AddRabbitMQ("rabbitmq")
    .WithDataVolume();

var redis = builder
    .AddRedis("redis")
    .WithDataVolume();

var postgres = builder
    .AddPostgres("postgres")
    .WithHostPort(6543)
    .WithDataVolume("postgres-data");

var identityDatabase = postgres.AddDatabase("identitydb");
var userDatabase = postgres.AddDatabase("userdb");
var contentDatabase = postgres.AddDatabase("contentdb");
var mediaDatabase = postgres.AddDatabase("mediadb");
var searchDatabase = postgres.AddDatabase("searchdb");

var minio = builder
    .AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", minioAccessKey)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioSecretKey)
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithExternalHttpEndpoints()
    .WithVolume("minio-data", "/data");

var identityService = builder
    .AddProject<Projects.RedNote_IdentityService>("identity-service")
    .WithEnvironment("Security__RequireHttps", "false")
    .WithEnvironment("Security__TrustAnyForwardedHeaders", "true")
    .WithReference(identityDatabase)
    .WaitFor(identityDatabase);


var userService = builder
    .AddProject<Projects.RedNote_UserService>("user-service")
    .WithReference(userDatabase)
    .WithReference(identityService)
    .WithReference(rabbitMq)
    .WaitFor(userDatabase)
    .WaitFor(identityService)
    .WaitFor(rabbitMq);

var mediaService = builder
    .AddProject<Projects.RedNote_MediaService>("media-service")
    .WithReference(mediaDatabase)
    .WithReference(identityService)
    .WaitFor(mediaDatabase)
    .WaitFor(identityService)
    .WaitFor(minio)
    .WithEnvironment("S3__ServiceUrl", minio.GetEndpoint("s3"))
    .WithEnvironment("S3__PublicServiceUrl", mediaPublicUrl)
    .WithEnvironment("S3__AccessKey", minioAccessKey)
    .WithEnvironment("S3__SecretKey", minioSecretKey)
    .WithEnvironment("S3__BucketName", "rednote-media");

var contentService = builder
    .AddProject<Projects.RedNote_ContentService>("content-service")
    .WithReference(contentDatabase)
    .WithReference(identityService)
    .WithReference(mediaService)
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq)
    .WaitFor(contentDatabase)
    .WaitFor(identityService)
    .WaitFor(mediaService);

// HTTP/2 without TLS needs a dedicated port inside the Docker network.
if (builder.ExecutionContext.IsPublishMode)
{
    mediaService.WithHttpEndpoint(targetPort: 8081, name: "grpc")
        .WithEnvironment("Kestrel__Endpoints__Http__Url", "http://0.0.0.0:8080")
        .WithEnvironment("Kestrel__Endpoints__Http__Protocols", "Http1")
        .WithEnvironment("Kestrel__Endpoints__Grpc__Url", "http://0.0.0.0:8081")
        .WithEnvironment("Kestrel__Endpoints__Grpc__Protocols", "Http2");
    contentService.WithEnvironment("Grpc__MediaAddress", mediaService.GetEndpoint("grpc"));
    identityService.PublishAsDockerComposeService((_, service) =>
        service.AddVolume(new Volume { Name = "identity-keys", Source = "identity-keys", Target = "/home/app", Type = "volume" }))
        .WithEnvironment("HOME", "/home/app");
}
else
{
    contentService.WithEnvironment("Grpc__MediaAddress", mediaService.GetEndpoint("https"));
}

var openSearch = builder
    .AddContainer("opensearch", "opensearchproject/opensearch", "3.8.0")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("DISABLE_SECURITY_PLUGIN", "true")
    .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
    .WithHttpEndpoint(port: 9200, targetPort: 9200, name: "http")
    .WithVolume("opensearch-data", "/usr/share/opensearch/data");

var searchService = builder
    .AddProject<Projects.RedNote_SearchService>("search-service")
    .WithReference(searchDatabase)
    .WithReference(openSearch.GetEndpoint("http"))
    .WithReference(rabbitMq)
    .WaitFor(searchDatabase)
    .WaitFor(rabbitMq)
    .WaitFor(openSearch)
    .WithEnvironment("OpenSearch__Url", openSearch.GetEndpoint("http"));

if (builder.ExecutionContext.IsPublishMode)
{
    // Wolverine owns this database's schema; ensure the database itself exists on a fresh volume.
    var searchDatabaseInit = builder.AddContainer("search-database-init", "postgres", "18.3")
        .WithEnvironment("PGHOST", "postgres")
        .WithEnvironment("PGUSER", "postgres")
        .WithEnvironment("PGPASSWORD", postgres.Resource.PasswordParameter!)
        .WithArgs("sh", "-c", "printf '%s\\n' \"SELECT 'CREATE DATABASE searchdb' WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'searchdb');\" '\\gexec' | psql -v ON_ERROR_STOP=1")
        .WaitFor(postgres)
        .PublishAsDockerComposeService((_, service) => service.Restart = "no");
    searchService.WaitForCompletion(searchDatabaseInit);
}

var gateway = builder
    .AddProject<Projects.RedNote_Gateway>("gateway")
    .WithReference(identityService)
    .WithReference(userService)
    .WithReference(contentService)
    .WithReference(mediaService)
    .WithReference(searchService)
    .WaitFor(identityService)
    .WaitFor(userService)
    .WaitFor(contentService)
    .WaitFor(mediaService)
    .WaitFor(searchService)
    .WithEnvironment("Cors__AllowedOrigins__0", frontendPublicUrl)
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http", isProxied: false)
    .WithExternalHttpEndpoints();

var jwtIssuer = ReferenceExpression.Create($"{gatewayPublicUrl}/");

var jwtMetadataAddress = ReferenceExpression.Create($"{gateway.GetEndpoint("http")}/.well-known/openid-configuration");

gateway
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Jwt__MetadataAddress", jwtMetadataAddress)
    .WithEnvironment("Jwt__RequireHttpsMetadata", "false");

userService
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Jwt__MetadataAddress", jwtMetadataAddress)
    .WithEnvironment("Jwt__RequireHttpsMetadata", "false");

contentService
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Jwt__MetadataAddress", jwtMetadataAddress)
    .WithEnvironment("Jwt__RequireHttpsMetadata", "false");

mediaService
    .WithEnvironment("Jwt__Issuer", jwtIssuer)
    .WithEnvironment("Jwt__Audience", jwtAudience)
    .WithEnvironment("Jwt__MetadataAddress", jwtMetadataAddress)
    .WithEnvironment("Jwt__RequireHttpsMetadata", "false");

#pragma warning disable ASPIREJAVASCRIPT001, ASPIREDOCKERFILEBUILDER001

var frontend = builder
    .AddViteApp("frontend", "../Red-Book")
    .WithNpm()
    .PublishAsNodeServer(entryPoint: ".output/server/index.mjs", outputPath: ".output")
    .WithDockerfileBaseImage(buildImage: "node:24-bookworm-slim", runtimeImage: "node:24-bookworm-slim")
    .WithReference(gateway)
    .WaitFor(gateway)
    .WithReference(redis)
    .WaitFor(redis)
    .WithHttpEndpoint(3000)
    .WithEnvironment("NUXT_GATEWAY_BASE_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("NUXT_PUBLIC_API_BASE_URL", gatewayPublicUrl)
    .WithEnvironment("NUXT_OAUTH_OIDC_CLIENT_ID", "rednote-web")
    .WithEnvironment("NUXT_OAUTH_OIDC_OPENID_CONFIG", ReferenceExpression.Create($"{gateway.GetEndpoint("http")}/.well-known/openid-configuration"))
    .WithEnvironment("NUXT_OAUTH_OIDC_REDIRECT_URL", ReferenceExpression.Create($"{frontendPublicUrl}/auth/rednote"))
    .WithEnvironment("NUXT_SESSION_PASSWORD", nuxtSessionPassword)
    .WithExternalHttpEndpoints();

gateway.WithEnvironment(
    "ReverseProxy__Clusters__frontend-cluster__Destinations__frontend__Address",
    frontend.GetEndpoint("http"));

// Explicit local Docker profile; does not weaken the production cookie default.
if (builder.ExecutionContext.IsPublishMode && builder.Configuration["LocalDocker"] == "true")
{
    identityService.WithEnvironment("Security__RequireHttps", "false")
        .WithEnvironment("Security__AllowHttpCookies", "true");
    frontend.WithEnvironment("NUXT_SESSION_COOKIE_SECURE", "false");
}

identityService
    .WithEnvironment("OpenIddict__PublicBaseUrl", gatewayPublicUrl)
    .WithEnvironment("OpenIddict__Clients__RedNoteWeb__LoginUri", ReferenceExpression.Create($"{frontendPublicUrl}/login"))
    .WithEnvironment("OpenIddict__Clients__RedNoteWeb__RedirectUri", ReferenceExpression.Create($"{frontendPublicUrl}/auth/rednote"))
    .WithEnvironment("OpenIddict__Clients__RedNoteWeb__PostLogoutRedirectUri", frontendPublicUrl);


var identityMigrations = identityService
    .AddEFMigrations("identity-migrations")
    .WithReference(identityDatabase)
    .WaitFor(identityDatabase)
    .RunDatabaseUpdateOnStart()
    .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
    .PublishAsDockerComposeService((_, service) => service.Restart = "no");

identityService.WaitForCompletion(identityMigrations);

var userMigrations = userService
    .AddEFMigrations("user-migrations")
    .WithReference(userDatabase)
    .WaitFor(userDatabase)
    .RunDatabaseUpdateOnStart()
    .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
    .PublishAsDockerComposeService((_, service) => service.Restart = "no");

userService.WaitForCompletion(userMigrations);

var mediaMigrations = mediaService
    .AddEFMigrations("media-migrations")
    .WithReference(mediaDatabase)
    .WaitFor(mediaDatabase)
    .RunDatabaseUpdateOnStart()
    .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
    .PublishAsDockerComposeService((_, service) => service.Restart = "no");

mediaService.WaitForCompletion(mediaMigrations);

var contentMigrations = contentService
    .AddEFMigrations("content-migrations")
    .WithReference(contentDatabase)
    .WaitFor(contentDatabase)
    .RunDatabaseUpdateOnStart()
    .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
    .PublishAsDockerComposeService((_, service) => service.Restart = "no");

contentService.WaitForCompletion(contentMigrations);

builder.Build().Run();