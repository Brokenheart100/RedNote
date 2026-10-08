using JasperFx.Aspire;
using RedNote.AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

builder.ConfigureRedNoteCompose();

var gatewayPublicUrl = builder.AddParameter(
    "gateway-public-url",
    builder.ExecutionContext.IsPublishMode ? "http://localhost:8080" : "https://localhost:8443",
    publishValueAsDefault: true,
    secret: false);

var frontendPublicUrl = builder.AddParameter(
    "frontend-public-url",
    builder.ExecutionContext.IsPublishMode ? "http://localhost:3000" : "https://localhost:8443",
    publishValueAsDefault: true,
    secret: false);

var mediaPublicUrl = builder.AddParameter("media-public-url",
    builder.ExecutionContext.IsPublishMode ? "http://localhost:9000" : "https://localhost:8443",
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
    .WithModule(RedisModules.Json)
    .WithModule(RedisModules.Search)
    .WithModule(RedisModules.TimeSeries)
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
var adminDatabase = postgres.AddDatabase("admindb");
var recommendationDatabase = postgres.AddDatabase("recommendationdb");

var minio = builder
    .AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", minioAccessKey)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioSecretKey)
    .WithHttpEndpoint(port: builder.ExecutionContext.IsPublishMode ? 9000 : null, targetPort: 9000, name: "s3")
    .WithHttpEndpoint(port: builder.ExecutionContext.IsPublishMode ? 9001 : null, targetPort: 9001, name: "console")
    .WithExternalHttpEndpoints()
    .WithVolume("minio-data", "/data");

var identityService = builder
    .AddProject<Projects.RedNote_IdentityService>("identity-service")
    .WithEnvironment("Security__RequireHttps", "false")
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

var recommendationService = builder.AddRedNoteRecommendations(postgres, redis, rabbitMq, contentService, recommendationDatabase);

builder.ConfigureMediaTransport(mediaService, contentService);
builder.ConfigureIdentityKeyPersistence(identityService);

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
    .WithReference(identityService)
    .WithReference(openSearch.GetEndpoint("http"))
    .WithReference(rabbitMq)
    .WaitFor(searchDatabase)
    .WaitFor(identityService)
    .WaitFor(rabbitMq)
    .WaitFor(openSearch)
    .WithEnvironment("OpenSearch__Url", openSearch.GetEndpoint("http"));

var adminService = builder.AddProject<Projects.RedNote_AdminService>("admin-service")
    .WithReference(adminDatabase).WaitFor(adminDatabase)
    .WithReference(identityService).WaitFor(identityService)
    .WithReference(contentService).WaitFor(contentService)
    .WithReference(userService).WaitFor(userService)
    .WithReference(rabbitMq).WaitFor(rabbitMq);

var gateway = builder
    .AddProject<Projects.RedNote_Gateway>("gateway")
    .WithReference(identityService)
    .WithReference(userService)
    .WithReference(contentService)
    .WithReference(mediaService)
    .WithReference(searchService)
    .WithReference(adminService)
    .WithReference(recommendationService)
    .WaitFor(identityService)
    .WaitFor(userService)
    .WaitFor(contentService)
    .WaitFor(mediaService)
    .WaitFor(searchService)
    .WaitFor(adminService)
    .WithEnvironment("Cors__AllowedOrigins__0", frontendPublicUrl)
    .WithExternalHttpEndpoints();

if (builder.ExecutionContext.IsPublishMode)
{
    gateway.WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http", isProxied: false);
}
else
{
    gateway.WithHttpsEndpoint(port: 8443, targetPort: 8443, name: "https", isProxied: false)
        .WithEnvironment("ReverseProxy__Clusters__minio-cluster__Destinations__minio__Address", minio.GetEndpoint("s3"));
    identityService.WithEnvironment("Security__RequireHttps", "true");

}

// Back-end readiness endpoints are required by YARP's active health probes.
foreach (var service in new[] { identityService, userService, contentService, mediaService, searchService, adminService, recommendationService })
    service.WithEnvironment("HealthChecks__Enabled", "true");

// Diagnostic commands use the service's resolved Aspire environment.
if (builder.ExecutionContext.IsRunMode)
{
    identityService.WithJasperFxCommands();
    userService.WithJasperFxCommands();
    contentService.WithJasperFxCommands();
    mediaService.WithJasperFxCommands();
    searchService.WithJasperFxCommands();
    adminService.WithJasperFxCommands();
    recommendationService.WithJasperFxCommands();
}

var gatewayInternalEndpoint = gateway.GetEndpoint(builder.ExecutionContext.IsPublishMode ? "http" : "https");

var jwtIssuer = ReferenceExpression.Create($"{gatewayPublicUrl}/");

var jwtMetadataAddress = ReferenceExpression.Create($"{gatewayInternalEndpoint}/.well-known/openid-configuration");

searchService.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);
identityService.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);
gateway.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);
userService.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);
contentService.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);
mediaService.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);
adminService.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);
recommendationService.WithJwtConfiguration(jwtIssuer, jwtAudience, jwtMetadataAddress);

#pragma warning disable ASPIREJAVASCRIPT001, ASPIREDOCKERFILEBUILDER001

#pragma warning disable ASPIREBROWSERLOGS001
var frontend = builder
    .AddViteApp("frontend", "../Red-Book")
    .WithBrowserLogs(browser: "msedge", userDataMode: BrowserUserDataMode.Isolated)
    .WithNpm()
    .PublishAsNodeServer(entryPoint: ".output/server/index.mjs", outputPath: ".output")
    .WithSharedBffInfrastructure()
    .WithReference(gateway)
    .WaitFor(gateway)
    .WithReference(redis)
    .WaitFor(redis)
    .WithHttpEndpoint(port: builder.ExecutionContext.IsPublishMode ? 3000 : null)
    .WithEnvironment("NUXT_GATEWAY_BASE_URL", gatewayInternalEndpoint)
    .WithEnvironment("NUXT_PUBLIC_API_BASE_URL", gatewayPublicUrl)
    .WithEnvironment("NUXT_OAUTH_OIDC_CLIENT_ID", "rednote-web")
    .WithEnvironment("NUXT_OAUTH_OIDC_OPENID_CONFIG", ReferenceExpression.Create($"{gatewayInternalEndpoint}/.well-known/openid-configuration"))
    .WithEnvironment("NUXT_OAUTH_OIDC_REDIRECT_URL", ReferenceExpression.Create($"{frontendPublicUrl}/auth/rednote"))
    .WithEnvironment("NUXT_SESSION_PASSWORD", nuxtSessionPassword)
    .WithExternalHttpEndpoints();

#pragma warning restore ASPIREBROWSERLOGS001
if (builder.ExecutionContext.IsRunMode)
{
    frontend.WithCertificateTrustScope(CertificateTrustScope.None)
        .WithEnvironment("NODE_USE_SYSTEM_CA", "1")
        .WithEnvironment("NODE_EXTRA_CA_CERTS", builder.ExportDevelopmentCertificate())
        .WithEnvironment("NUXT_SESSION_COOKIE_SECURE", "true");
}

gateway.WithEnvironment("ReverseProxy__Clusters__frontend-cluster__Destinations__frontend__Address", frontend.GetEndpoint("http"));
builder.AddRedNoteAdministration(gateway, identityService, redis, gatewayInternalEndpoint, gatewayPublicUrl);

// Explicit local Docker profile; does not weaken the production cookie default.
if (builder.ExecutionContext.IsPublishMode && builder.Configuration["LocalDocker"] == "true")
{
    identityService.WithEnvironment("Security__AllowHttpCookies", "true");
    frontend.WithEnvironment("NUXT_SESSION_COOKIE_SECURE", "false");
}

identityService
    .WithEnvironment("OpenIddict__PublicBaseUrl", gatewayPublicUrl)
    .WithEnvironment("OpenIddict__Clients__RedNoteWeb__LoginUri", ReferenceExpression.Create($"{frontendPublicUrl}/login"))
    .WithEnvironment("OpenIddict__Clients__RedNoteWeb__RedirectUri", ReferenceExpression.Create($"{frontendPublicUrl}/auth/rednote"))
    .WithEnvironment("OpenIddict__Clients__RedNoteWeb__PostLogoutRedirectUri", frontendPublicUrl);


// Register migrations after all project configuration and diagnostic commands.
// The migration tooling captures project execution configuration during registration.
identityService.AddDatabaseMigrations(identityDatabase, "identity-migrations");
userService.AddDatabaseMigrations(userDatabase, "user-migrations");
mediaService.AddDatabaseMigrations(mediaDatabase, "media-migrations");
contentService.AddDatabaseMigrations(contentDatabase, "content-migrations");
searchService.AddDatabaseMigrations(searchDatabase, "search-migrations");
adminService.AddDatabaseMigrations(adminDatabase, "admin-migrations");
recommendationService.AddDatabaseMigrations(recommendationDatabase, "recommendation-migrations");

builder.Build().Run();
