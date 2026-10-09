using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace RedNote.AppHost.Extensions;

internal static class AdminApplicationExtensions
{
    public static void AddRedNoteAdministration(this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> gateway, IResourceBuilder<ProjectResource> identity,
        IResourceBuilder<RedisResource> redis, EndpointReference internalGateway,
        IResourceBuilder<ParameterResource> publicGateway)
    {
        var clientSecret = builder.AddParameter("admin-oidc-secret", secret: true);
        var sessionPassword = builder.AddParameter("admin-session-password", secret: true);
        var publicAdmin = ReferenceExpression.Create($"{publicGateway}/admin");
#pragma warning disable ASPIREJAVASCRIPT001, ASPIREDOCKERFILEBUILDER001
        var admin = builder.AddViteApp("admin", "../RedNote.Admin")
            .WithDevelopmentBrowserLogs()
            .WithNpm()
            .PublishAsNodeServer(entryPoint: ".output/server/index.mjs", outputPath: ".output")
            .WithSharedBffInfrastructure()
            .WithHttpEndpoint().WithReference(redis).WaitFor(redis).WithReference(gateway).WaitFor(gateway)
            .WithEnvironment("NUXT_GATEWAY_BASE_URL", internalGateway)
            .WithEnvironment("NUXT_PUBLIC_ADMIN_BASE_URL", publicAdmin)
            .WithEnvironment("NUXT_OAUTH_OIDC_CLIENT_ID", "rednote-admin")
            .WithEnvironment("NUXT_OAUTH_OIDC_CLIENT_SECRET", clientSecret)
            .WithEnvironment("NUXT_OAUTH_OIDC_OPENID_CONFIG", ReferenceExpression.Create($"{internalGateway}/.well-known/openid-configuration"))
            .WithEnvironment("NUXT_OAUTH_OIDC_REDIRECT_URL", ReferenceExpression.Create($"{publicAdmin}/auth/rednote"))
            .WithEnvironment("NUXT_SESSION_PASSWORD", sessionPassword);
#pragma warning restore ASPIREJAVASCRIPT001, ASPIREDOCKERFILEBUILDER001
        if (builder.ExecutionContext.IsRunMode)
            admin.WithCertificateTrustScope(CertificateTrustScope.None).WithEnvironment("NODE_EXTRA_CA_CERTS", builder.ExportDevelopmentCertificate())
                .WithEnvironment("NODE_USE_SYSTEM_CA", "1");
        if (builder.ExecutionContext.IsRunMode && builder.Configuration.GetValue<bool>("CODESPACES"))
            admin.WithEnvironment("NUXT_CODESPACES_PROXY_ORIGIN", "https://localhost:8443");
        if (builder.ExecutionContext.IsPublishMode && builder.Configuration["LocalDocker"] == "true")
            admin.WithEnvironment("NUXT_SESSION_COOKIE_SECURE", "false");
        gateway.WithEnvironment("ReverseProxy__Clusters__admin-cluster__Destinations__admin__Address", admin.GetEndpoint("http"));
        identity.WithEnvironment("OpenIddict__Clients__RedNoteAdmin__ClientSecret", clientSecret)
            .WithEnvironment("OpenIddict__Clients__RedNoteAdmin__LoginUri", ReferenceExpression.Create($"{publicAdmin}/login"))
            .WithEnvironment("OpenIddict__Clients__RedNoteAdmin__RedirectUri", ReferenceExpression.Create($"{publicAdmin}/auth/rednote"))
            .WithEnvironment("OpenIddict__Clients__RedNoteAdmin__PostLogoutRedirectUri", publicAdmin);
    }
}
