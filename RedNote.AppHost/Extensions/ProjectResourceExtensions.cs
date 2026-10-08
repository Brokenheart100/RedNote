using Aspire.Hosting.ApplicationModel;

namespace RedNote.AppHost.Extensions;

internal static class ProjectResourceExtensions
{
    public static IResourceBuilder<ProjectResource> WithJwtConfiguration(
        this IResourceBuilder<ProjectResource> service,
        ReferenceExpression issuer,
        string audience,
        ReferenceExpression metadataAddress)
    {
        return service
            .WithEnvironment("Jwt__Issuer", issuer)
            .WithEnvironment("Jwt__Audience", audience)
            .WithEnvironment("Jwt__MetadataAddress", metadataAddress)
            .WithEnvironment("Jwt__RequireHttpsMetadata", "false");
    }

    public static void AddDatabaseMigrations(
        this IResourceBuilder<ProjectResource> service,
        IResourceBuilder<PostgresDatabaseResource> database,
        string name)
    {
        var migrations = service.AddEFMigrations(name)
            .WithReference(database)
            .WaitFor(database)
            .RunDatabaseUpdateOnStart()
            .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
            .PublishAsDockerComposeService((_, composeService) => composeService.Restart = "no");

        service.WaitForCompletion(migrations);
    }
}
