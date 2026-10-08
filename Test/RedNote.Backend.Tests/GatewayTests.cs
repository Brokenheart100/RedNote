using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedNote.Gateway.Extensions;
using ServiceDefaults;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class GatewayTests
{
    [Fact]
    public async Task AnonymousReadsWorkButPrivateAndWriteRoutesStopAtGateway()
    {
        await WithGateway(async client =>
        {
            foreach (var path in (string[])[ "/api/v1/posts/feed", "/api/v1/posts", "/api/v1/search/posts", "/.well-known/openid-configuration", "/api/v1/media/11111111-1111-1111-1111-111111111111", "/api/v1/users/11111111-1111-1111-1111-111111111111/followers"])
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
            foreach (var path in (string[])[ "/api/v1/users/me", "/api/v1/users/me/following/ids", "/api/v1/search/history", "/api/v1/posts/liked", "/api/v1/posts/favorites"])
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/posts", new StringContent("{}"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/v1/posts/11111111-1111-1111-1111-111111111111")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/posts/batch", new StringContent("{}"))).StatusCode);
        });
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/connect/token")]
    [InlineData("/api/search/posts")]
    [InlineData("/api/media/images")]
    [InlineData("/admin/api/login")]
    [InlineData("/api/v1/auth/admin/login")]
    [InlineData("/admin/api/manage/session")]
    public async Task BffAndBackendPathsReturn429WithRetryHint(string path)
    {
        await WithGateway(async client =>
        {
            for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(path, new StringContent("{}"))).StatusCode);
            var rejected = await client.PostAsync(path, new StringContent("{}"));
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.NotNull(rejected.Headers.RetryAfter);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
        }, permits: 2);
    }

    [Fact]
    public async Task UnreadyUpstreamIsExcludedAndRecoversAfterHealthProbe()
    {
        var ready = false;
        await WithGateway(async client =>
        {
            await WaitForStatus(HttpStatusCode.ServiceUnavailable);
            ready = true;
            await WaitForStatus(HttpStatusCode.OK);

            async Task WaitForStatus(HttpStatusCode expected)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                HttpStatusCode actual;
                do
                {
                    actual = (await client.GetAsync("/api/v1/posts/feed")).StatusCode;
                    if (actual == expected) return;
                    await Task.Delay(50);
                } while (DateTime.UtcNow < deadline);
                Assert.Equal(expected, actual);
            }
        }, readiness: () => ready);
    }

    [Fact]
    public void ProductionRejectsTrustingEveryProxy()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration["Security:TrustAnyForwardedHeaders"] = "true";
        Assert.Throws<InvalidOperationException>(() => builder.AddTrustedForwardedHeaders());
    }

    [Fact]
    public async Task ProductionRejectsCredentialedAnyOriginCors()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => WithGateway(_ => Task.CompletedTask, anyOrigin: true));
    }

    [Fact]
    public async Task UntrustedForwardedIpCannotCreateNewLimitPartitions()
    {
        await WithGateway(async client =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
                request.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
                var response = await client.SendAsync(request);
                Assert.Equal(i < 2 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
            }
        }, permits: 2, simulateUntrustedPeer: true);
    }

    private static async Task WithGateway(Func<HttpClient, Task> assertions, int permits = 1000, bool anyOrigin = false, bool simulateUntrustedPeer = false, Func<bool>? readiness = null)
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/health", () => readiness?.Invoke() == false ? Results.StatusCode(503) : Results.Ok());
        upstream.Map("/{**path}", () => Results.Ok(new { source = "test-upstream" }));
        await upstream.StartAsync();
        var address = upstream.Urls.Single();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "RedNote.Gateway"))) root = root.Parent;
        builder.Configuration.AddJsonFile(Path.Combine(root!.FullName, "RedNote.Gateway/appsettings.json"));
        foreach (var cluster in builder.Configuration.GetSection("ReverseProxy:Clusters").GetChildren())
        {
            builder.Configuration[$"{cluster.Path}:HealthCheck:Active:Interval"] = "00:00:00.100";
            foreach (var destination in cluster.GetSection("Destinations").GetChildren())
                builder.Configuration[$"{destination.Path}:Address"] = address;
        }
        builder.Configuration["Jwt:Issuer"] = address;
        builder.Configuration["Jwt:Audience"] = "test-api";
        builder.Configuration["Jwt:MetadataAddress"] = address + "/.well-known/openid-configuration";
        builder.Configuration["Jwt:RequireHttpsMetadata"] = "false";
        foreach (var bucket in new[] { "Auth", "Search", "Upload", "Api" })
            builder.Configuration[$"RateLimiting:{bucket}PermitsPerMinute"] = permits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        builder.Configuration["Cors:AllowAnyOrigin"] = anyOrigin.ToString();
        builder.AddTrustedForwardedHeaders();
        builder.Services.AddServiceDiscovery();
        builder.Services.AddHealthChecks();
        builder.Services.AddGatewayServices(builder.Configuration, builder.Environment);
        await using var app = builder.Build();
        if (simulateUntrustedPeer) app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10"); return next(context); });
        app.UseForwardedHeaders();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapDefaultEndpoints();
        app.MapReverseProxy();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await assertions(client);
    }
}


