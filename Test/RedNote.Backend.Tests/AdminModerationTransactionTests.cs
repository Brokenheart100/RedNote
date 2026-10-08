using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Alba;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RedNote.Authentication;
using RedNote.ContentService.Features.Admin;
using RedNote.ContentService.Infrastructure.Persistence;
using RedNote.Contracts.Admin;
using RedNote.Contracts.Content;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Http.ApiVersioning;
using Wolverine.Postgresql;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class AdminModerationTransactionTests(BackendFixture fixture) : IAsyncLifetime
{
    private string _connection = null!;
    private IAlbaHost _host = null!;
    private HttpClient _client = null!;
    private readonly Guid _post = Guid.NewGuid();
    public async Task InitializeAsync()
    {
        _connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"admin_moderation_{Guid.NewGuid():N}" }.ConnectionString;
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            db.Posts.Add(new RedNote.ContentService.Domain.Posts.Post(_post, Guid.NewGuid(), "test", "test"));
            await db.SaveChangesAsync();
        }
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDefaultProblemDetails();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, UserHeaderAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(AdminAuthorization.Moderate, policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddWolverineHttp();
        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ModerationEndpoints).Assembly;
            options.UseRuntimeCompilation();
            options.UseFluentValidation();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(AdminAuditEventSink));
            options.PersistMessagesWithPostgresql(_connection);
            options.Services.AddDbContextWithWolverineIntegration<ContentServiceDbContext>(db => db.UseNpgsql(_connection));
            options.Durability.EnableDeduplicatedResponses = true;
            options.PublishMessage<AdminAuditRecorded>().ToLocalQueue("audit").UseDurableInbox();
            options.PublishMessage<PostVisibilityChanged>().ToLocalQueue("audit");
        });
        _host = await AlbaHost.For(builder, app =>
        {
            app.UseDefaultExceptionHandler(exception => exception is Microsoft.AspNetCore.Http.BadHttpRequestException bad ? bad.StatusCode : 500);
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapWolverineEndpoints(options =>
            {
                options.CustomizeHttpEndpointDiscovery(query => query.Excludes.WithCondition("moderation only", type => type != typeof(ModerationEndpoints)));
                options.UseFluentValidationProblemDetailMiddleware();
                options.UseApiVersioning(v => v.UnversionedPolicy = UnversionedPolicy.PassThrough);
            });
        });
        _client = _host.Server.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task ReplayDoesNotRepeatModerationOrAuditAndChangedPayloadIsRejected()
    {
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await Hide(key, "reason")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Hide(key, "reason")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Hide(key, "changed")).StatusCode);
        await using var db = CreateDb();
        Assert.True((await db.Posts.SingleAsync()).IsHidden);
        Assert.Equal(2, (await db.Posts.SingleAsync()).Revision);
        Assert.Equal(1, await db.AdminAudit.CountAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!AdminAuditEventSink.Events.Any(x => x.TargetId == _post)) await Task.Delay(50, timeout.Token);
        Assert.Single(AdminAuditEventSink.Events, x => x.TargetId == _post);
    }

    [Fact]
    public async Task FailedAuditCommitRollsBackModerationAndLeavesNoPublishedAudit()
    {
        await using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdminAudit\" ADD CONSTRAINT test_admin_audit_failure CHECK (\"Reason\" <> 'fail')");
        Assert.Equal(HttpStatusCode.InternalServerError, (await Hide(Guid.NewGuid().ToString(), "fail")).StatusCode);
        Assert.False((await db.Posts.SingleAsync()).IsHidden);
        Assert.Equal(0, await db.AdminAudit.CountAsync());
        Assert.DoesNotContain(AdminAuditEventSink.Events, x => x.TargetId == _post);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM wolverine.wolverine_incoming_envelopes").SingleAsync());
    }

    [Theory]
    [InlineData("{\"revision\":1}", "reason")]
    [InlineData("{\"revision\":1,\"reason\":\"   \"}", "reason")]
    [InlineData("{\"revision\":0,\"reason\":\"valid\"}", "revision")]
    [InlineData("null", "body")]
    public async Task InvalidModerationReturnsFieldErrorsWithoutMutation(string json, string field)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/admin/posts/{_post}/hide")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        await using var db = CreateDb();
        Assert.False((await db.Posts.SingleAsync()).IsHidden);
        Assert.Equal(1, (await db.Posts.SingleAsync()).Revision);
        Assert.Equal(0, await db.AdminAudit.CountAsync());
    }

    [Fact]
    public async Task OversizedReasonAndUnknownActionCannotMutatePost()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Hide(Guid.NewGuid().ToString(), new string('a', 501))).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/admin/posts/{_post}/unknown")
            { Content = JsonContent.Create(new ModerationRequest(1, "valid")) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(request)).StatusCode);
        await using var db = CreateDb();
        Assert.Equal(0, await db.AdminAudit.CountAsync());
        Assert.Equal(1, (await db.Posts.SingleAsync()).Revision);
    }

    private async Task<HttpResponseMessage> Hide(string key, string reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/admin/posts/{_post}/hide")
            { Content = JsonContent.Create(new { revision = 1, reason }) };
        request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request);
    }
    private ContentServiceDbContext CreateDb() => new(new DbContextOptionsBuilder<ContentServiceDbContext>().UseNpgsql(_connection).Options);
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_connection is not null) { await using var db = CreateDb(); await db.Database.EnsureDeletedAsync(); }
    }
}

public static class AdminAuditEventSink
{
    public static readonly ConcurrentQueue<AdminAuditRecorded> Events = new();
    public static void Handle(AdminAuditRecorded message) => Events.Enqueue(message);
    public static void Handle(PostVisibilityChanged _) { }
}
