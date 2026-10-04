using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace RedNote.Authentication;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddRedNoteJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<JwtAuthenticationOptions>()
            .Bind(
                configuration.GetSection(
                    JwtAuthenticationOptions.SectionName))
            .Validate(
                static options =>
                    IsAbsoluteHttpUri(options.Issuer),
                "Jwt:Issuer must be an absolute HTTP or HTTPS URI.")
            .Validate(
                static options =>
                    !string.IsNullOrWhiteSpace(options.Audience),
                "Jwt:Audience must not be empty.")
            .Validate(
                static options =>
                    IsAbsoluteHttpUri(options.MetadataAddress),
                "Jwt:MetadataAddress must be an absolute HTTP or HTTPS URI.")
            .ValidateOnStart();

        services.AddSingleton<
            IConfigureOptions<JwtBearerOptions>,
            ConfigureJwtBearerOptions>();

        services
            .AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddAuthorization();

        return services;
    }

    private static bool IsAbsoluteHttpUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ConfigureJwtBearerOptions(
        IOptions<JwtAuthenticationOptions> authenticationOptions,
        ILoggerFactory loggerFactory)
        : IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly JwtAuthenticationOptions _authenticationOptions =
            authenticationOptions.Value;

        public void Configure(
            JwtBearerOptions options)
        {
            Configure(
                JwtBearerDefaults.AuthenticationScheme,
                options);
        }

        public void Configure(
            string? name,
            JwtBearerOptions options)
        {
            if (!string.Equals(
                    name,
                    JwtBearerDefaults.AuthenticationScheme,
                    StringComparison.Ordinal))
            {
                return;
            }

            options.MetadataAddress = _authenticationOptions.MetadataAddress;

            options.RequireHttpsMetadata = _authenticationOptions.RequireHttpsMetadata;

            options.MapInboundClaims = false;
            options.SaveToken = false;

            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer =
                        _authenticationOptions.Issuer,

                    ValidateAudience = true,
                    ValidAudience =
                        _authenticationOptions.Audience,

                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidTypes =
                    [
                        "at+jwt",
                        "application/at+jwt"
                    ],

                    NameClaimType = "name",
                    RoleClaimType = "role"
                };

            options.Events =
                CreateJwtBearerEvents(
                    loggerFactory);
        }
    }

    private static JwtBearerEvents CreateJwtBearerEvents(
        ILoggerFactory loggerFactory)
    {
        var logger =
            loggerFactory.CreateLogger(
                "RedNote.Authentication.JwtBearer");

        return new JwtBearerEvents
        {
            OnTokenValidated =
                context =>
                {
                    var subject =
                        context.Principal?
                            .FindFirst("sub")
                            ?.Value;

                    logger.LogDebug(
                        "🔐 JWT validated. Subject={Subject}, Path={Path}, TraceId={TraceId}",
                        subject,
                        context.HttpContext.Request.Path,
                        context.HttpContext.TraceIdentifier);

                    return Task.CompletedTask;
                },

            OnAuthenticationFailed =
                context =>
                {
                    logger.LogWarning(
                        context.Exception,
                        "❌ JWT authentication failed. Path={Path}, TraceId={TraceId}",
                        context.HttpContext.Request.Path,
                        context.HttpContext.TraceIdentifier);

                    return Task.CompletedTask;
                },

            OnChallenge =
                context =>
                {
                    logger.LogDebug(
                        "🔐 JWT challenge. Error={Error}, Description={Description}, Path={Path}, TraceId={TraceId}",
                        context.Error,
                        context.ErrorDescription,
                        context.HttpContext.Request.Path,
                        context.HttpContext.TraceIdentifier);

                    return Task.CompletedTask;
                }
        };
    }
}