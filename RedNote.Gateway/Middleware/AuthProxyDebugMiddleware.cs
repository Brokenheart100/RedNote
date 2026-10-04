using System.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace RedNote.Gateway.Middleware;

public sealed class AuthProxyDebugMiddleware(
    RequestDelegate next,
    ILogger<AuthProxyDebugMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<AuthProxyDebugMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

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

        var origin = request.Headers.Origin.ToString();

        _logger.LogInformation(
            "➡️ [GATEWAY AUTH DEBUG] {Method} {Path} | Scheme={Scheme} | Host={Host} | Origin={Origin} | HasIdentityCookie={HasIdentityCookie} | CookieCount={CookieCount}",
            request.Method,
            request.Path + request.QueryString,
            request.Scheme,
            request.Host.Value,
            string.IsNullOrWhiteSpace(origin) ? null : origin,
            hasIdentityCookie,
            request.Cookies.Count);

        try
        {
            await _next(context);
        }
        finally
        {
            var setCookies = context.Response.Headers.SetCookie;
            var hasIdentitySetCookie = ContainsIdentityCookie(setCookies);
            var hasAntiforgerySetCookie = ContainsAntiforgeryCookie(setCookies);

            _logger.LogInformation(
                "⬅️ [GATEWAY AUTH DEBUG] {Method} {Path} | StatusCode={StatusCode} | SetCookieCount={SetCookieCount} | HasIdentitySetCookie={HasIdentitySetCookie} | HasAntiforgerySetCookie={HasAntiforgerySetCookie} | DurationMs={DurationMs:F1}",
                request.Method,
                request.Path,
                context.Response.StatusCode,
                setCookies.Count,
                hasIdentitySetCookie,
                hasAntiforgerySetCookie,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private static bool ContainsIdentityCookie(StringValues headers)
    {
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (header.StartsWith("RedNote.Identity=", StringComparison.OrdinalIgnoreCase)
                || header.StartsWith("__Host-RedNote.Identity=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAntiforgeryCookie(StringValues headers)
    {
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (header.StartsWith("RedNote.Antiforgery=", StringComparison.OrdinalIgnoreCase)
                || header.StartsWith("__Host-RedNote.Antiforgery=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}