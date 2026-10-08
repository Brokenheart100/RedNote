using Aspire.Hosting.JavaScript;

namespace RedNote.AppHost.Extensions;

internal static class BffApplicationExtensions
{
    public static IResourceBuilder<TResource> WithSharedBffInfrastructure<TResource>(
        this IResourceBuilder<TResource> resource) where TResource : JavaScriptAppResource
    {
        var builder = resource.ApplicationBuilder;
        if (builder.ExecutionContext.IsPublishMode)
        {
            // AddViteApp already registered the build pipeline. Replace its Dockerfile annotation
            // without registering that pipeline again (Aspire 13.5.4 duplicates pipeline annotations).
            foreach (var existing in resource.Resource.Annotations.OfType<DockerfileBuildAnnotation>().ToArray())
                resource.Resource.Annotations.Remove(existing);
            var root = Path.GetFullPath("..", builder.AppHostDirectory);
            var dockerfile = new DockerfileBuildAnnotation(root, Path.Combine(root, "Dockerfile.nuxt"), stage: null);
            dockerfile.BuildArguments["APP"] = Path.GetFileName(resource.Resource.WorkingDirectory);
            resource.Resource.Annotations.Add(dockerfile);
        }

        if (builder.ExecutionContext.IsRunMode)
        {
            if (!builder.TryCreateResourceBuilder<ExecutableResource>("bff-shared-build", out var shared))
                shared = builder.AddExecutable("bff-shared-build", "npm", "../RedNote.Bff.Shared", "ci")
                    .ExcludeFromManifest();

            // Installers also package the local dependency, so they must wait for its compiled output.
            if (resource.Resource.TryGetLastAnnotation<JavaScriptPackageInstallerAnnotation>(out var installer))
                builder.CreateResourceBuilder(installer.Resource).WaitForCompletion(shared);
            resource.WaitForCompletion(shared);
        }

        return resource;
    }
}
