using Alba;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RedNote.RecommendationService;
using ServiceDefaults;
using StackExchange.Redis;
using Xunit;

namespace RedNote.Backend.Tests;

public class RecommendationAvailabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheAvailabilityErrorsReturn503ForFrontendFallback(bool timeout)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDefaultProblemDetails();
        await using var host = await AlbaHost.For(builder, app =>
        {
            app.UseRecommendationExceptionHandler();
            app.MapGet("/cache", () => Task.FromException<IResult>(timeout
                ? new RedisTimeoutException("Cache request timed out", CommandStatus.WaitingToBeSent)
                : new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Cache connection unavailable")));
        });
        await host.Scenario(s => { s.Get.Url("/cache"); s.StatusCodeShouldBe(503); });
    }
}
