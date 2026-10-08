using Aspire.Hosting.Docker.Resources.ServiceNodes;

namespace RedNote.AppHost.Extensions;

internal static class DockerComposeExtensions
{
    public static void ConfigureRedNoteCompose(this IDistributedApplicationBuilder builder)
    {
        builder.AddDockerComposeEnvironment("compose")
          .WithProperties(env =>
            {
                env.DashboardEnabled = true;
            })
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
    }
}
