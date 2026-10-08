using System.Diagnostics;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace RedNote.Gateway.Middleware;

public sealed class AuthProxyDebugMiddleware(
    RequestDelegate next,
    ILogger<AuthProxyDebugMiddleware> logger,
    IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var isAuthentication = request.Path.StartsWithSegments("/api/v1/auth")
            || request.Path.StartsWithSegments("/connect")
            || request.Path.StartsWithSegments("/.well-known");
        var isApi = configuration.GetValue("Diagnostics:GatewayDebug:LogAllApiRequests", true)
            && request.Path.StartsWithSegments("/api");
        if (!isAuthentication && !isApi)
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = traceId,
            ["RequestId"] = Clean(request.Headers["X-Request-ID"].ToString())
        });

        logger.LogInformation(
            "➡️ 🌐 [GATEWAY] {Method} {Path} | TraceId={TraceId} | Scheme={Scheme} | Host={Host} | Origin={Origin} | ContentType={ContentType} | ContentLength={ContentLength}",
            request.Method, request.Path, traceId, request.Scheme,
            Clean(request.Host.Value), SafeUrl(request.Headers.Origin.ToString()),
            Clean(request.ContentType), request.ContentLength);
        logger.LogInformation(
            "🔀 📡 [GATEWAY] ForwardedProto={ForwardedProto} | ForwardedHost={ForwardedHost} | RemoteIP={RemoteIP} | QueryKeys={QueryKeys}",
            Clean(request.Headers["X-Forwarded-Proto"].ToString()),
            Clean(request.Headers["X-Forwarded-Host"].ToString()),
            context.Connection.RemoteIpAddress,
            string.Join(", ", request.Query.Keys.Select(Clean)));
        logger.LogInformation(
            "🔐 🍪 [GATEWAY] HasAuthorization={HasAuthorization} | HasCsrfToken={HasCsrfToken} | CookieCount={CookieCount} | CookieNames={CookieNames} | HasIdentityCookie={HasIdentityCookie}",
            request.Headers.ContainsKey("Authorization"), request.Headers.ContainsKey("X-CSRF-TOKEN"),
            request.Cookies.Count, string.Join(", ", request.Cookies.Keys.Select(Clean)),
            request.Cookies.Keys.Any(IsIdentityCookie));

        var pipelineFailed = false;
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            pipelineFailed = true;
            // Exception messages may contain upstream URLs or credentials.
            logger.LogError(
                "💥 [GATEWAY] Pipeline failed | TraceId={TraceId} | ExceptionType={ExceptionType}",
                traceId, exception.GetType().Name);
            throw;
        }
        finally
        {
            var proxy = context.Features.Get<IReverseProxyFeature>();
            var forwarderError = context.Features.Get<IForwarderErrorFeature>();
            logger.LogInformation(
                "🎯 🚚 [GATEWAY] Route={Route} | Cluster={Cluster} | Destination={Destination} | ForwarderError={ForwarderError} | IsAuthenticated={IsAuthenticated}",
                proxy?.Route.Config.RouteId, proxy?.Cluster.Config.ClusterId,
                SafeUrl(proxy?.ProxiedDestination?.Model.Config.Address),
                forwarderError?.Error, context.User.Identity?.IsAuthenticated == true);
            logger.LogInformation(
                "⬅️ {Outcome} [GATEWAY] {Method} {Path} | TraceId={TraceId} | StatusCode={StatusCode} | DurationMs={DurationMs:F1} | ContentType={ContentType} | Location={Location}",
                pipelineFailed ? "💥" : context.Response.StatusCode >= 400 ? "⚠️" : "✅", request.Method,
                request.Path, traceId, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Clean(context.Response.ContentType), SafeUrl(context.Response.Headers.Location.ToString()));
            foreach (var cookie in context.Response.Headers.SetCookie)
            {
                logger.LogInformation("📤 🍪 [GATEWAY] SetCookie={CookieSummary}", SummarizeCookie(cookie));
            }
        }
    }

    private static bool IsIdentityCookie(string name) =>
        name.Equals("RedNote.Identity", StringComparison.Ordinal)
        || name.StartsWith("RedNote.IdentityC", StringComparison.Ordinal)
        || name.Equals("__Host-RedNote.Identity", StringComparison.Ordinal);

    private static string SummarizeCookie(string? header)
    {
        var parts = (header ?? string.Empty).Split(';', StringSplitOptions.TrimEntries);
        var name = parts[0].Split('=', 2)[0];
        var attributes = parts.Skip(1).Select(part => part.Split('=', 2)[0]).ToArray();
        var sameSite = parts.Skip(1)
            .FirstOrDefault(part => part.StartsWith("SameSite=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1];
        var safeSameSite = sameSite?.ToUpperInvariant() is "LAX" or "STRICT" or "NONE" ? sameSite : "-";
        return $"{Clean(name)}=<redacted>; Secure={attributes.Contains("Secure", StringComparer.OrdinalIgnoreCase)}; HttpOnly={attributes.Contains("HttpOnly", StringComparer.OrdinalIgnoreCase)}; SameSite={safeSameSite}; Attributes=[{string.Join(", ", attributes.Select(Clean))}]";
    }

    private static string SafeUrl(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return Clean(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped));
        }
        // Relative OIDC redirects may include authorization codes and state.
        return Clean(value.Split('?', '#')[0]);
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ');
        return cleaned.Length > 300 ? cleaned[..300] : cleaned;
    }
}
