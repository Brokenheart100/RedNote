using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace RedNote.Authentication;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddRedNoteJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 3. 将 Validate 中的 IsAbsoluteHttpUri 替换为一行 lambda 表达式
        services.AddOptions<JwtAuthenticationOptions>()
            .Bind(configuration.GetSection(JwtAuthenticationOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.Issuer, UriKind.Absolute, out var i) && (i.Scheme == Uri.UriSchemeHttp || i.Scheme == Uri.UriSchemeHttps), "Jwt:Issuer must be an absolute HTTP(S) URI.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience must not be empty.")
            .Validate(o => Uri.TryCreate(o.MetadataAddress, UriKind.Absolute, out var m) && (m.Scheme == Uri.UriSchemeHttp || m.Scheme == Uri.UriSchemeHttps), "Jwt:MetadataAddress must be an absolute HTTP(S) URI.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // 4. 干掉 ConfigureJwtBearerOptions 内部类！直接利用 Options 管道注入 IOptions 和 ILoggerFactory
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtAuthenticationOptions>, ILoggerFactory>((options, jwtOptions, loggerFactory) =>
            {
                var opt = jwtOptions.Value;
                options.MetadataAddress = opt.MetadataAddress;
                options.RequireHttpsMetadata = opt.RequireHttpsMetadata;
                options.MapInboundClaims = false;
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    opt.MetadataAddress, new OpenIdConnectConfigurationRetriever(),
                    new InternalOidcDocumentRetriever(opt));

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = opt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidTypes = ["at+jwt", "application/at+jwt"],
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        loggerFactory.CreateLogger("Jwt").LogWarning(
                            context.Exception,
                            "❌ JWT authentication failed. Path={Path}, TraceId={TraceId}",
                            context.HttpContext.Request.Path,
                            context.HttpContext.TraceIdentifier);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }
}

// Discovery advertises public URLs, while server-side metadata and JWKS use the internal gateway.
internal sealed class InternalOidcDocumentRetriever(JwtAuthenticationOptions options) : IDocumentRetriever
{
    private readonly HttpDocumentRetriever retriever = new() { RequireHttps = options.RequireHttpsMetadata };

    public Task<string> GetDocumentAsync(string address, CancellationToken cancellationToken)
    {
        var requested = new Uri(address);
        var issuer = new Uri(options.Issuer);
        var metadata = new Uri(options.MetadataAddress);
        if (requested.GetLeftPart(UriPartial.Authority) == issuer.GetLeftPart(UriPartial.Authority))
            requested = new Uri(new Uri(metadata.GetLeftPart(UriPartial.Authority)), requested.PathAndQuery);
        return retriever.GetDocumentAsync(requested.AbsoluteUri, cancellationToken);
    }
}
