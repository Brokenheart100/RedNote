using RedNote.Authentication;
using RedNote.Gateway.Configuration;

namespace RedNote.Gateway.Extensions;

internal static class GatewayServiceCollectionExtensions
{
    internal const string FrontendCorsPolicy = "FrontendCors";

    /// <summary>注册 Gateway 所需的服务。</summary>
    internal static IServiceCollection AddGatewayServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(configuration);
        services.AddProblemDetails();
        services.AddRedNoteJwtAuthentication(configuration);
        services.AddAdminAuthorization();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
            options.FallbackPolicy = options.DefaultPolicy;
        });
        if (!environment.IsDevelopment() && configuration["AllowedHosts"]?.Split(';').Contains("*") == true)
            throw new InvalidOperationException("Configure explicit AllowedHosts outside Development.");
        services.AddGatewayRateLimiting(configuration);
        AddGatewayCors(services, configuration, environment);
        services.AddReverseProxy().LoadFromConfig(configuration.GetRequiredSection("ReverseProxy")).AddServiceDiscoveryDestinationResolver();
        return services;
    }

    private static void AddGatewayCors(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var corsSection = configuration.GetRequiredSection(GatewayCorsOptions.SectionName);
        services.AddOptions<GatewayCorsOptions>().Bind(corsSection)
            .Validate(o => environment.IsDevelopment() || !o.AllowAnyOrigin, "AllowAnyOrigin is only permitted in Development.")
            .Validate(o => o.AllowAnyOrigin || o.AllowedOrigins.Length > 0, "Configure AllowAnyOrigin=true or at least one allowed origin.")
            .Validate(o => o.AllowAnyOrigin || o.AllowedOrigins.All(IsValidOrigin), "Every CORS origin must be an absolute HTTP or HTTPS origin without a path.")
            .ValidateOnStart();

        var corsOptions = corsSection.Get<GatewayCorsOptions>() ?? throw new InvalidOperationException($"Configuration section '{GatewayCorsOptions.SectionName}' is invalid.");

        services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy =>
        {
            if (corsOptions.AllowAnyOrigin) policy.SetIsOriginAllowed(_ => true);
            else policy.WithOrigins(corsOptions.AllowedOrigins);
            policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
    }

    private static bool IsValidOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        var isSupportedScheme = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var hasNoPath = uri.AbsolutePath is "/"; var hasNoQuery = string.IsNullOrEmpty(uri.Query);
        var hasNoFragment = string.IsNullOrEmpty(uri.Fragment); var hasNoTrailingSlash = !origin.EndsWith('/');
        return isSupportedScheme && hasNoPath && hasNoQuery && hasNoFragment && hasNoTrailingSlash;
    }
}
