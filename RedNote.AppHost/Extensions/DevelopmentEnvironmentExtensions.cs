using Aspire.Hosting.JavaScript;
using Microsoft.Extensions.Configuration;

namespace RedNote.AppHost.Extensions;

internal static class DevelopmentEnvironmentExtensions
{
    public static string GetDevelopmentPublicOrigin(this IDistributedApplicationBuilder builder)
    {
        if (builder.ExecutionContext.IsPublishMode || !builder.Configuration.GetValue<bool>("CODESPACES"))
            return "https://localhost:8443";

        var name = builder.Configuration["CODESPACE_NAME"];
        var domain = builder.Configuration["GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN"];
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException("Codespaces requires CODESPACE_NAME and GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN.");

        // Aspire rewrites dashboard links; explicit OIDC/JWT/S3 parameters also need the public origin.
        return $"https://{name}-8443.{domain}";
    }

    public static void ConfigureCodespacesGateway(this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> gateway, IResourceBuilder<ParameterResource> publicUrl)
    {
        if (!builder.ExecutionContext.IsRunMode || !builder.Configuration.GetValue<bool>("CODESPACES"))
            return;

        gateway.WithEnvironment(async context =>
        {
            var origin = await publicUrl.Resource.GetValueAsync(context.CancellationToken);
            // Keep host filtering enabled, including internal calls and the one forwarded public host.
            context.EnvironmentVariables["AllowedHosts"] = $"localhost;127.0.0.1;gateway;{new Uri(origin!).Host}";
        });
    }

    public static IResourceBuilder<TResource> WithDevelopmentBrowserLogs<TResource>(
        this IResourceBuilder<TResource> resource) where TResource : JavaScriptAppResource
    {
        // Browser instrumentation needs a local desktop browser. Codespaces uses the user's browser.
        if (!OperatingSystem.IsWindows() || resource.ApplicationBuilder.Configuration.GetValue<bool>("CODESPACES"))
            return resource;

#pragma warning disable ASPIREBROWSERLOGS001
        return resource.WithBrowserLogs(browser: "msedge", userDataMode: BrowserUserDataMode.Isolated);
#pragma warning restore ASPIREBROWSERLOGS001
    }
}
