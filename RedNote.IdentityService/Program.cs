using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RedNote.IdentityService.Domain.Users;
using RedNote.IdentityService.Infrastructure.OpenIddict;
using RedNote.IdentityService.Infrastructure.Persistence;
using RedNote.IdentityService.Middleware;
using ServiceDefaults;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var isDevelopment = builder.Environment.IsDevelopment();

var requireHttps = builder.Configuration.GetValue(
    "Security:RequireHttps",
    !isDevelopment);

var trustAnyForwardedHeaders = builder.Configuration.GetValue(
    "Security:TrustAnyForwardedHeaders",
    isDevelopment);

var connectionString =
    builder.Configuration.GetConnectionString("identitydb")
    ?? throw new InvalidOperationException(
        "Connection string 'identitydb' was not found.");

builder.Services.AddDbContext<IdentityServiceDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseOpenIddict();
});

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<IdentityServiceDbContext>();

/*
 * Identity Application Cookie
 *
 * 这里不再使用 Security:RequireHttps 控制 Cookie。
 *
 * 原因：
 *
 * Browser -> Cloudflare -> Gateway 是 HTTPS；
 * Gateway -> IdentityService 可以是内部 HTTP。
 *
 * SameAsRequest 会依据 Forwarded Headers 处理之后的 Request.Scheme：
 *
 * - 公网 HTTPS 请求：写入 Secure Cookie。  
 * - 内部 HTTP 请求：不会因为 SecurePolicy.Always 直接失败。
 *
 * SameSite=None 用于 Frontend 与 Gateway 不同 Origin 的认证流程。
 */
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "RedNote.Identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";

    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;

    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
});

builder.Services.Configure<BearerTokenOptions>(
    IdentityConstants.BearerScheme,
    options =>
    {
        options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
        options.RefreshTokenExpiration = TimeSpan.FromDays(14);
    });

/*
 * Antiforgery Cookie
 *
 * 同样使用 SameAsRequest。
 *
 * 这同时支持：
 *
 * 1. Browser -> Cloudflare HTTPS -> Gateway -> IdentityService
 * 2. Nuxt BFF -> Gateway HTTP -> IdentityService
 *
 * 从而避免之前：
 *
 * SecurePolicy=Always
 * + 内部 HTTP
 * => GetAndStoreTokens() 抛 InvalidOperationException
 */
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";

    options.Cookie.Name = "RedNote.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";

    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

var openIddictPublicBaseUrl =
    builder.Configuration["OpenIddict:PublicBaseUrl"]
    ?? throw new InvalidOperationException(
        "Configuration 'OpenIddict:PublicBaseUrl' was not found.");

if (!Uri.TryCreate(
        openIddictPublicBaseUrl,
        UriKind.Absolute,
        out var openIddictIssuer))
{
    throw new InvalidOperationException(
        $"Configuration 'OpenIddict:PublicBaseUrl' is not a valid absolute URI: '{openIddictPublicBaseUrl}'.");
}

if (!openIddictIssuer.AbsoluteUri.EndsWith('/'))
{
    openIddictIssuer = new Uri($"{openIddictIssuer.AbsoluteUri}/", UriKind.Absolute);
}
builder.Services
    .AddOpenIddict()
    .AddCore(options =>
    {
        options
            .UseEntityFrameworkCore()
            .UseDbContext<IdentityServiceDbContext>();
    })
    .AddServer(options =>
    {
        /*
         * Issuer 与所有浏览器可见 OIDC endpoint
         * 一律使用公网绝对地址。
         *
         * 防止 discovery 根据内部 Host 生成：
         *
         * http://gateway:8080/connect/authorize
         */
        options.SetIssuer(openIddictIssuer);

        options.SetAuthorizationEndpointUris(
            new Uri(openIddictIssuer, "connect/authorize"));

        options.SetTokenEndpointUris(
            new Uri(openIddictIssuer, "connect/token"));

        options.SetEndSessionEndpointUris(
            new Uri(openIddictIssuer, "connect/logout"));

        options.AllowAuthorizationCodeFlow();
        options.AllowRefreshTokenFlow();

        options.RequireProofKeyForCodeExchange();

        options.RegisterScopes(
            "openid",
            "profile",
            "email",
            "offline_access",
            "rednote-api");

        /*
         * 当前用于开发 / Docker smoke test。
         *
         * 真正生产环境必须替换为持久化的正式
         * signing / encryption certificates。
         */
        options.AddDevelopmentEncryptionCertificate();
        options.AddDevelopmentSigningCertificate();

        options.DisableAccessTokenEncryption();

        var aspNetCore = options
            .UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough();

        /*
         * requireHttps=false 只控制 OpenIddict
         * 对当前内部 transport 的 HTTPS 要求。
         *
         * 它不再控制浏览器 Cookie。
         */
        if (!requireHttps)
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }
    });

builder.Services.AddSingleton<OpenIddictSeeder>();

builder.Services.AddAuthorization();

/*
 * Forwarded Headers
 *
 * Cloudflare / Gateway 在外部 HTTPS -> 内部 HTTP 的情况下，
 * ASP.NET Core 必须能够根据 X-Forwarded-Proto 恢复原始 scheme。
 *
 * 本地 Docker + Cloudflare smoke test 可以显式打开
 * Security:TrustAnyForwardedHeaders。
 *
 * 正式生产环境不要使用 trust-all，
 * 应配置明确的 KnownProxies / KnownIPNetworks。
 */
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;

    options.ForwardLimit = 2;

    if (trustAnyForwardedHeaders)
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

builder.Services.AddWolverineHttp();

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();

    options.CodeGeneration
        .AlwaysUseServiceLocationFor<IdentityServiceDbContext>();

    options.CodeGeneration
        .AlwaysUseServiceLocationFor<UserManager<ApplicationUser>>();

    options.CodeGeneration
        .AlwaysUseServiceLocationFor<SignInManager<ApplicationUser>>();
});

var app = builder.Build();

/*
 * 必须位于 Authentication / Authorization 之前，
 * 这样后面的 Cookie / OpenIddict 才能看到恢复后的 Scheme/Host。
 */
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMiddleware<AuthDebugMiddleware>();
}

await app.Services
    .GetRequiredService<OpenIddictSeeder>()
    .SeedAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapWolverineEndpoints(options =>
{
    options.UseApiVersioning(versioning =>
    {
        versioning.UrlSegmentPrefix = "api/v{version}";
        versioning.UnversionedPolicy = UnversionedPolicy.PassThrough;
    });
});

app.MapDefaultEndpoints();

await app.RunAsync();