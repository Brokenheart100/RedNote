using System.Net;
using System.Net.Http.Json;
using Alba;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RedNote.AdminService.Features.Audit;
using RedNote.AdminService.Features.Management;
using RedNote.AdminService.Infrastructure.Clients;
using RedNote.AdminService.Infrastructure.Persistence;
using RedNote.Authentication;
using RedNote.Contracts.Admin;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Postgresql;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class AdminServiceTests(BackendFixture fixture) : IAsyncLifetime
{
    private string _connection = null!;
    private IAlbaHost _host = null!;
    private HttpClient _client = null!;
    public async Task InitializeAsync()
    {
        _connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"admin_projection_{Guid.NewGuid():N}" }.ConnectionString;
        await using (var db = CreateDb()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDefaultProblemDetails();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, UserHeaderAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var permission in new[] { AdminAuthorization.Audit, AdminAuthorization.Moderate, AdminAuthorization.Users })
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser());
        });
        builder.Services.AddScoped<AdminBusinessClient>();
        foreach (var service in new[] { "content-service", "user-service" })
            builder.Services.AddHttpClient(service, client => client.BaseAddress = new Uri("http://business.example"))
                .ConfigurePrimaryHttpMessageHandler(() => new ManagementStub());
        builder.Services.AddWolverineHttp();
        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(AuditEndpoints).Assembly;
            options.UseRuntimeCompilation();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(AdminAuditRecordedHandler));
            options.PersistMessagesWithPostgresql(_connection);
            options.Services.AddDbContextWithWolverineIntegration<AdminServiceDbContext>(db => db.UseNpgsql(_connection));
        });
        _host = await AlbaHost.For(builder, app =>
        {
            app.UseDefaultExceptionHandler();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapWolverineEndpoints(options => options.CustomizeHttpEndpointDiscovery(query => query.Excludes.WithCondition(
                "admin routes", type => type != typeof(AuditEndpoints) && type != typeof(ManagementEndpoints))));
        });
        _client = _host.Server.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task ActualManagementEndpointBindsJsonBodyAndForwardsAKeyedWrite()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/posts/{Guid.NewGuid()}/hide")
            { Content = JsonContent.Create(new { revision = 1, reason = "moderation" }) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        request.Headers.Add("Idempotency-Key", "test-key");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task DuplicateEventsAndBackfillKeepOriginalRecordAndSeparateSources()
    {
        var audit = Event("content");
        using var scope = _host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(audit);
        await bus.InvokeAsync(audit with { Reason = "must not overwrite" });
        await bus.InvokeAsync(audit with { Source = "user" });
        await using var db = CreateDb();
        Assert.Equal(2, await db.Audit.CountAsync());
        Assert.All(await db.Audit.ToListAsync(), entry => Assert.Equal(audit.Reason, entry.Reason));
        var json = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/admin/audit?actor={audit.ActorUserId}");
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var from = Uri.EscapeDataString(audit.CreatedAtUtc.AddMinutes(-1).ToOffset(TimeSpan.FromHours(8)).ToString("O"));
        var to = Uri.EscapeDataString(audit.CreatedAtUtc.AddMinutes(1).ToOffset(TimeSpan.FromHours(8)).ToString("O"));
        var dated = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/admin/audit?from={from}&to={to}");
        Assert.Equal(2, dated.GetProperty("total").GetInt32());
        var filtered = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/admin/content-audit?target={audit.TargetId}");
        Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/v1/admin/audit?source=unknown")).StatusCode);
    }

    [Fact]
    public async Task InvalidImportRollsBackAllRecordsAndSuccessfulImportCanBeRepeated()
    {
        var audit = Event("content") with { CreatedAtUtc = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)) };
        var file = Path.GetTempFileName();
        try
        {
            await using var db = CreateDb();
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(new[] { audit, Event("invalid") }));
            await Assert.ThrowsAsync<ArgumentException>(() => AuditImport.ImportAsync(file, db));
            Assert.Equal(0, await db.Audit.CountAsync());
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(new[] { audit }));
            Assert.Equal(1, await AuditImport.ImportAsync(file, db));
            Assert.Equal(1, await AuditImport.ImportAsync(file, db));
            Assert.Equal(1, await db.Audit.CountAsync());
            var imported = await db.Audit.SingleAsync();
            Assert.Equal(TimeSpan.Zero, imported.CreatedAtUtc.Offset);
            // PostgreSQL timestamp precision is microseconds.
            Assert.True((imported.CreatedAtUtc - audit.CreatedAtUtc).Duration() < TimeSpan.FromMilliseconds(1));
        }
        finally { File.Delete(file); }
    }

    private static AdminAuditRecorded Event(string source) => new(Guid.NewGuid(), source, Guid.NewGuid(), "post.hide", "post",
        Guid.NewGuid(), "review reason", "Hidden=True", "trace", DateTimeOffset.UtcNow);
    private sealed class ManagementStub : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.StartsWith("/internal/admin/posts/", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-token", request.Headers.Authorization!.Parameter);
            Assert.Equal("test-key", request.Headers.GetValues("Idempotency-Key").Single());
            var body = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
            Assert.Equal("moderation", body.GetProperty("reason").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { action = "post.hide" }) };
        }
    }
    private AdminServiceDbContext CreateDb() => new(new DbContextOptionsBuilder<AdminServiceDbContext>().UseNpgsql(_connection).Options);
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_connection is not null) { await using var db = CreateDb(); await db.Database.EnsureDeletedAsync(); }
    }
}
