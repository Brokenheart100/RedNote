using Microsoft.EntityFrameworkCore;
using ServiceDefaults;
using StackExchange.Redis;

namespace RedNote.RecommendationService;

public static class RecommendationHttpConfiguration
{
    public static void UseRecommendationExceptionHandler(this WebApplication app) =>
        app.UseDefaultExceptionHandler(ex => ex switch
        {
            BadHttpRequestException bad => bad.StatusCode,
            DbUpdateException => 409,
            RedisConnectionException or RedisTimeoutException => 503,
            HttpRequestException or OperationCanceledException => 503,
            _ => 500
        });
}
