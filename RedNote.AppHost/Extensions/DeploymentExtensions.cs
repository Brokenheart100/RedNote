using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

namespace RedNote.AppHost.Extensions;

internal static class DeploymentExtensions
{
    public static void ConfigureMediaTransport(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> mediaService,
        IResourceBuilder<ProjectResource> contentService)
    {
        if (builder.ExecutionContext.IsPublishMode)
        {
            // MediaService configures the matching container listeners in C#.
            mediaService.WithHttpEndpoint(targetPort: 8081, name: "grpc");
            contentService.WithEnvironment("Grpc__MediaAddress", mediaService.GetEndpoint("grpc"));
        }
        else
        {
            contentService.WithEnvironment("Grpc__MediaAddress", mediaService.GetEndpoint("https"));
        }
    }

    public static void ConfigureIdentityKeyPersistence(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> identityService)
    {
        if (!builder.ExecutionContext.IsPublishMode) return;

        identityService.PublishAsDockerComposeService((_, service) =>
            service.AddVolume(new Volume
            {
                Name = "identity-keys",
                Source = "identity-keys",
                Target = "/home/app",
                Type = "volume"
            }))
            .WithEnvironment("HOME", "/home/app");
    }
}
