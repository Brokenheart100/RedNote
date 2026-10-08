using System.Globalization;
using System.Threading.RateLimiting;

namespace RedNote.Gateway.Extensions;

internal static class GatewayRateLimitingExtensions
{
    internal static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("RateLimiting");
        var limits = new Dictionary<string, int>
        {
            ["auth"] = section.GetValue("AuthPermitsPerMinute", 30),
            ["search"] = section.GetValue("SearchPermitsPerMinute", 120),
            ["upload"] = section.GetValue("UploadPermitsPerMinute", 30),
            ["api"] = section.GetValue("ApiPermitsPerMinute", 600),
            ["recommendation-feedback"] = section.GetValue("RecommendationFeedbackPermitsPerMinute", 120)
        };
        if (limits.Values.Any(limit => limit <= 0))
            throw new InvalidOperationException("RateLimiting permits must be positive.");

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                string? bucket = null;
                if (HttpMethods.IsPost(context.Request.Method)
                    && (path == "/api/auth/login" || path == "/api/auth/register"
                        || path == "/api/v1/auth/login" || path == "/api/v1/auth/register"
                        || path == "/admin/api/login" || path == "/api/v1/auth/admin/login" || path == "/connect/token"))
                    bucket = "auth";
                else if (path == "/api/v1/posts/recommendations/feedback") bucket = "recommendation-feedback";
                else if (path.StartsWithSegments("/api/search") || path.StartsWithSegments("/api/v1/search")) bucket = "search";
                else if (HttpMethods.IsPost(context.Request.Method)
                    && (path == "/api/media/images" || path == "/api/v1/media/images")) bucket = "upload";
                else if (path.StartsWithSegments("/api") || path.StartsWithSegments("/connect")
                         || path.StartsWithSegments("/admin/api")) bucket = "api";

                if (bucket is null) return RateLimitPartition.GetNoLimiter("unlimited");
                // Only RemoteIpAddress is used; forwarded headers are resolved by the trusted-proxy middleware.
                var identity = bucket == "recommendation-feedback" ? context.User.FindFirst("sub")?.Value : null;
                var key = $"{bucket}:{identity ?? context.Connection.RemoteIpAddress?.ToString()}";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits[bucket],
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await Results.Problem(statusCode: 429, title: "Too many requests.").ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}
