using System.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace RedNote.IdentityService.Middleware;

public sealed class AuthDebugMiddleware(
    RequestDelegate next,
    ILogger<AuthDebugMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<AuthDebugMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;

        var shouldLog =
            request.Path.StartsWithSegments("/api/v1/auth")
            || request.Path.StartsWithSegments("/connect")
            || request.Path.StartsWithSegments("/.well-known");

        if (!shouldLog)
        {
            await _next(context);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();

        var hasIdentityCookie =
            request.Cookies.ContainsKey("RedNote.Identity")
            || request.Cookies.ContainsKey("__Host-RedNote.Identity");

        var forwardedProto =
            request.Headers.TryGetValue("X-Forwarded-Proto", out var proto)
                ? proto.ToString()
                : null;

        var forwardedHost =
            request.Headers.TryGetValue("X-Forwarded-Host", out var host)
                ? host.ToString()
                : null;

        _logger.LogInformation(
            "➡️ [AUTH DEBUG] {Method} {Path} | Scheme={Scheme} | Host={Host} | XForwardedProto={XForwardedProto} | XForwardedHost={XForwardedHost} | HasIdentityCookie={HasIdentityCookie}",
            request.Method,
            request.Path + request.QueryString,
            request.Scheme,
            request.Host.Value,
            forwardedProto,
            forwardedHost,
            hasIdentityCookie);

        try
        {
            await _next(context);
        }
        finally
        {
            var elapsed =
                Stopwatch.GetElapsedTime(startedAt);

            var setCookieHeaders =
                response.Headers.SetCookie;

            var hasIdentitySetCookie =
                ContainsIdentityCookie(setCookieHeaders);

            _logger.LogInformation(
                "⬅️ [AUTH DEBUG] {Method} {Path} | StatusCode={StatusCode} | SetCookieCount={SetCookieCount} | HasIdentitySetCookie={HasIdentitySetCookie} | DurationMs={DurationMs}",
                request.Method,
                request.Path,
                response.StatusCode,
                setCookieHeaders.Count,
                hasIdentitySetCookie,
                elapsed.TotalMilliseconds);
        }
    }

    private static bool ContainsIdentityCookie(
        StringValues setCookieHeaders)
    {
        foreach (var header in setCookieHeaders)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (
                header.StartsWith(
                    "RedNote.Identity=",
                    StringComparison.OrdinalIgnoreCase)
                || header.StartsWith(
                    "__Host-RedNote.Identity=",
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }
}