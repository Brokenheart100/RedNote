using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using RedNote.AdminService.Infrastructure.Clients;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class AdminForwardingTests
{
    [Fact]
    public async Task ParallelWritesKeepEachAdministratorsTokenAndKeyAndPreserveTheExternal204Contract()
    {
        var received = new ConcurrentBag<(string? Token, string Key, string Path)>();
        using var handler = new RecordingHandler(request =>
        {
            received.Add((request.Headers.Authorization?.Parameter, request.Headers.GetValues("Idempotency-Key").Single(), request.RequestUri!.PathAndQuery));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"action\":\"post.hide\"}") };
        });
        var client = new AdminBusinessClient(new ClientFactory(handler), NullLogger<AdminBusinessClient>.Instance);
        var first = Context("first", "first-key");
        var second = Context("second", "second-key");
        var body = JsonSerializer.SerializeToElement(new { revision = 1, reason = "review" });
        var results = await Task.WhenAll(client.ForwardAsync(first, "content-service", "/internal/admin/posts/one/hide", body),
            client.ForwardAsync(second, "content-service", "/internal/admin/posts/two/hide", body));
        Assert.All(results, result => Assert.Equal(204, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode));
        Assert.Contains(("first", "first-key", "/internal/admin/posts/one/hide?page=2"), received);
        Assert.Contains(("second", "second-key", "/internal/admin/posts/two/hide?page=2"), received);
    }

    [Fact]
    public async Task AmbiguousWriteFailureIsReturnedWithoutRepeatingTheRequest()
    {
        var calls = 0;
        using var handler = new RecordingHandler(_ => { calls++; throw new HttpRequestException("connection lost"); });
        var client = new AdminBusinessClient(new ClientFactory(handler), NullLogger<AdminBusinessClient>.Instance);
        var result = await client.ForwardAsync(Context("actor", "key"), "content-service", "/internal/admin/posts/one/hide",
            JsonSerializer.SerializeToElement(new { revision = 1, reason = "review" }));
        Assert.Equal(503, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(1, calls);
    }

    private static DefaultHttpContext Context(string token, string key)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        context.Request.Headers["Idempotency-Key"] = key;
        context.Request.QueryString = new QueryString("?page=2");
        return context;
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://business.example") };
    }
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(action(request));
    }
}
