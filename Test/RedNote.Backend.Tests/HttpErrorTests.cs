using System.Net.Http.Json;
using System.Text.Json;
using Alba;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceDefaults;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class HttpErrorTests
{
    [Theory]
    [InlineData("unexpected", 500)]
    [InlineData("concurrency", 409)]
    [InlineData("subject", 401)]
    public async Task ExceptionsReturnProblemDetailsWithTraceIdWithoutLeakingDetails(string kind, int expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.SetMinimumLevel(LogLevel.Critical);
        builder.Services.AddDefaultProblemDetails();
        await using var host = await AlbaHost.For(builder, app =>
        {
            app.UseDefaultExceptionHandler(exception => exception switch
            {
                DbUpdateConcurrencyException => 409,
                UnauthorizedAccessException => 401,
                _ => 500
            });
            app.MapGet("/failure/{kind}", (string kind) => Failure(kind));
        });
        using var client = host.Server.CreateClient();
        using var response = await client.GetAsync($"/failure/{kind}");
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("internal-sensitive-details", body);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    private static IResult Failure(string kind) => throw (kind switch
    {
        "concurrency" => new DbUpdateConcurrencyException("internal-sensitive-details"),
        "subject" => new UnauthorizedAccessException("internal-sensitive-details"),
        _ => new InvalidOperationException("internal-sensitive-details")
    });
}
