var builder = DistributedApplication.CreateBuilder(args);

var compose = builder
    .AddDockerComposeEnvironment("compose")
    .ConfigureComposeFile(composeFile =>
    {
        composeFile.Name = "rednote";
    });

var publicOrigin = builder.AddParameter(
    "public-origin",
    "http://localhost:8080",
    publishValueAsDefault: true,
    secret: false);

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
    .AddRabbitMQ("rabbitmq");

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
    .WithEnvironment("S3__AccessKey", minioAccessKey)
    .WithEnvironment("S3__SecretKey", minioSecretKey)
    .WithEnvironment("S3__BucketName", "rednote-media");

var contentService = builder
    .AddProject<Projects.RedNote_ContentService>("content-service")
    .WithReference(contentDatabase)
    .WithReference(identityService)
    .WithReference(mediaService)
    .WithReference(userService)
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq)
    .WaitFor(contentDatabase)
    .WaitFor(identityService)
    .WaitFor(userService)
    .WaitFor(mediaService);

var openSearch = builder
    .AddContainer("opensearch", "opensearchproject/opensearch", "3.8.0")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("DISABLE_SECURITY_PLUGIN", "true")
    .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
    .WithHttpEndpoint(port: 9200, targetPort: 9200, name: "http")
    .WithVolume("opensearch-data", "/usr/share/opensearch/data");

var searchService = builder
    .AddProject<Projects.RedNote_SearchService>("search-service")
    .WithReference(contentService)
    .WithReference(searchDatabase)
    .WithReference(openSearch.GetEndpoint("http"))
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq)
    .WaitFor(contentService)
    .WaitFor(openSearch)
    .WithEnvironment("OpenSearch__Url", openSearch.GetEndpoint("http"));

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

#pragma warning disable ASPIREJAVASCRIPT001

var frontend = builder
    .AddViteApp("frontend", "../Red-Book")
    .WithNpm()
    .PublishAsNodeServer(entryPoint: ".output/server/index.mjs", outputPath: ".output")
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

var searchMigrations = searchService
    .AddEFMigrations("search-migrations")
    .WithReference(searchDatabase)
    .WaitFor(searchDatabase)
    .RunDatabaseUpdateOnStart()
    .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
    .PublishAsDockerComposeService((_, service) => service.Restart = "no");

searchService.WaitForCompletion(searchMigrations);

builder.Build().Run();



// cloudflared tunnel --url http://localhost:3000
// cloudflared tunnel --url http://localhost:8080



// & "C:\Program Files (x86)\cloudflared\cloudflared.exe"  service install eyJhIjoiYmM1ZDJmZGU2ZTVhNjBmZGI1ODUyNjdhM2U0ZjY1ZmQiLCJ0IjoiNjBmY2E4MGYtOTFkYi00ZjkzLWJjMGYtNzk0MzQ4NmIyZDY5IiwicyI6IlkyVmlZMkl4T0RFdFl6WTJPQzAwTVdNMExUbGhaak10TkdFMlpqSTBPVGhoTVRsaCJ9
