using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using Alba;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RedNote.Contracts.Users;
using RedNote.Authentication;
using RedNote.Contracts.Admin;
using RedNote.UserService.Features.Admin;
using RedNote.UserService.Features.Users.GetMe;
using RedNote.UserService.Features.Users.UpdateMe;
using RedNote.UserService.Infrastructure.Persistence;
using ServiceDefaults;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Http.FluentValidation;
using Wolverine.Postgresql;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class UserTransactionTests(BackendFixture fixture) : IAsyncLifetime
{
    private string _connection = null!;
    private IAlbaHost _host = null!;
    private HttpClient _client = null!;
    private readonly Guid _user = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            { Database = $"user_transactions_{Guid.NewGuid():N}" }.ConnectionString;
        await using (var db = CreateUserDb()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Services.AddDefaultProblemDetails();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, UserHeaderAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(AdminAuthorization.Users, policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddWolverineHttp();
        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(GetMeEndpoint).Assembly;
            options.UseRuntimeCompilation();
            options.UseFluentValidation();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(ProfileEventSink));
            options.Discovery.IncludeType(typeof(AdminAuditEventSink));
            options.PersistMessagesWithPostgresql(_connection);
            options.Durability.EnableDeduplicatedResponses = true;
            options.Services.AddDbContextWithWolverineIntegration<UserServiceDbContext>(db => db.UseNpgsql(_connection));
            options.PublishMessage<UserProfileChanged>().ToLocalQueue("profile").UseDurableInbox();
            options.PublishMessage<AdminAuditRecorded>().ToLocalQueue("audit").UseDurableInbox();
        });
        _host = await AlbaHost.For(builder, app =>
        {
            app.UseDefaultExceptionHandler(exception => exception is Microsoft.AspNetCore.Http.BadHttpRequestException bad ? bad.StatusCode : 500);
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapWolverineEndpoints(options =>
            {
                options.CustomizeHttpEndpointDiscovery(query => query.Excludes.WithCondition("profile endpoints only",
                    type => type != typeof(GetMeEndpoint) && type != typeof(UpdateMeEndpoint) && type != typeof(UserRestrictionEndpoints)));
                options.UseFluentValidationProblemDetailMiddleware();
                options.UseApiVersioning(versioning => versioning.UrlSegmentPrefix = "api/v{version}");
            });
        });
        _client = _host.Server.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", _user.ToString());
    }

    [Fact]
    public async Task FrameworkCommitsProfileAndPublishesEvent()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/users/me")).StatusCode);
        await WaitForEvent("same display name");
        var update = await _client.PatchAsJsonAsync("/api/v1/users/me", new UpdateMeRequest("updated profile", null, "bio"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        await using var db = CreateUserDb();
        Assert.Equal("updated profile", (await db.UserProfiles.SingleAsync(profile => profile.UserId == _user)).Nickname);
        await WaitForEvent("updated profile");
    }

    [Fact]
    public async Task FailedCommitDoesNotPersistProfileOrPublishItsEvent()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/users/me")).StatusCode);
        await WaitForEvent("same display name");
        await using var db = CreateUserDb();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            while (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM wolverine.wolverine_incoming_envelopes WHERE status <> 'Handled'").SingleAsync(timeout.Token) != 0)
                await Task.Delay(50, timeout.Token);
        var messagesBefore = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM wolverine.wolverine_incoming_envelopes").SingleAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"UserProfiles\" ADD CONSTRAINT test_profile_failure CHECK (\"Nickname\" <> 'failed profile')");
        try
        {
            var failed = await _client.PatchAsJsonAsync("/api/v1/users/me", new UpdateMeRequest("failed profile", null, null));
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.DoesNotContain("test_profile_failure", await failed.Content.ReadAsStringAsync());
            Assert.Equal("same display name", (await db.UserProfiles.AsNoTracking().SingleAsync()).Nickname);
            // The durable queue and EF writes share a transaction, so no outgoing row may survive.
            var pending = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM wolverine.wolverine_incoming_envelopes").SingleAsync();
            Assert.Equal(messagesBefore, pending);
            Assert.DoesNotContain(ProfileEventSink.Messages, message => message.UserId == _user && message.Nickname == "failed profile");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"UserProfiles\" DROP CONSTRAINT test_profile_failure");
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.PatchAsJsonAsync("/api/v1/users/me",
            new UpdateMeRequest("retry profile", null, null))).StatusCode);
        await WaitForEvent("retry profile");
    }

    [Fact]
    public async Task RestrictionReplayPersistsOneAuditAndPublishesOneEvent()
    {
        await using var db = CreateUserDb();
        db.UserProfiles.Add(new RedNote.UserService.Domain.Users.UserProfile(_user));
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await Restrict(key, "reason")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Restrict(key, "reason")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Restrict(key, "changed")).StatusCode);
        Assert.Equal(1, (await db.UserRestrictions.SingleAsync()).Revision);
        Assert.Equal(1, await db.AdminAudit.CountAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!AdminAuditEventSink.Events.Any(x => x.TargetId == _user)) await Task.Delay(50, timeout.Token);
        Assert.Single(AdminAuditEventSink.Events, x => x.TargetId == _user);
    }

    [Fact]
    public async Task FailedRestrictionAuditCommitLeavesNoRestrictionOrEvent()
    {
        await using var db = CreateUserDb();
        db.UserProfiles.Add(new RedNote.UserService.Domain.Users.UserProfile(_user));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdminAudit\" ADD CONSTRAINT test_restriction_failure CHECK (\"Reason\" <> 'fail')");
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.InternalServerError, (await Restrict(key, "fail")).StatusCode);
        Assert.Equal(0, await db.UserRestrictions.CountAsync());
        Assert.Equal(0, await db.AdminAudit.CountAsync());
        Assert.DoesNotContain(AdminAuditEventSink.Events, x => x.TargetId == _user);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM wolverine.wolverine_incoming_envelopes").SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await Restrict(key, "fixed")).StatusCode);
    }

    [Theory]
    [InlineData("", 0, false, "reason")]
    [InlineData("   ", 0, false, "reason")]
    [InlineData("valid", -1, false, "revision")]
    [InlineData("valid", 0, true, "expiresAtUtc")]
    public async Task InvalidRestrictionReturnsFieldErrorsWithoutAudit(string reason, long revision, bool expired, string field)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/admin/users/{_user}/restrictions")
        {
            Content = JsonContent.Create(new RestrictionRequest(true, true,
                expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : null, revision, reason))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        await using var db = CreateUserDb();
        Assert.Equal(0, await db.UserRestrictions.CountAsync());
        Assert.Equal(0, await db.AdminAudit.CountAsync());
    }

    [Fact]
    public async Task RestrictionAcceptsFutureExpiryWithNonUtcOffset()
    {
        await using var db = CreateUserDb();
        db.UserProfiles.Add(new RedNote.UserService.Domain.Users.UserProfile(_user));
        await db.SaveChangesAsync();
        var expiry = DateTimeOffset.UtcNow.AddHours(2).ToOffset(TimeSpan.FromHours(8));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/admin/users/{_user}/restrictions")
            { Content = JsonContent.Create(new RestrictionRequest(true, false, expiry, 0, "valid")) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request)).StatusCode);
        var stored = (await db.UserRestrictions.SingleAsync()).ExpiresAtUtc!.Value;
        Assert.Equal(TimeSpan.Zero, stored.Offset);
        // PostgreSQL timestamps have microsecond precision; .NET has 100ns ticks.
        Assert.InRange((expiry - stored).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(9));
    }

    private async Task<HttpResponseMessage> Restrict(string key, string reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/admin/users/{_user}/restrictions")
        {
            Content = JsonContent.Create(new RestrictionRequest(true, true, null, 0, reason))
        };
        request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request);
    }

    private async Task WaitForEvent(string nickname)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            if (ProfileEventSink.Messages.Any(message => message.UserId == _user && message.Nickname == nickname)) return;
            await Task.Delay(50, timeout.Token);
        }
    }

    private UserServiceDbContext CreateUserDb() => new(new DbContextOptionsBuilder<UserServiceDbContext>().UseNpgsql(_connection).Options);
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_connection is not null) { await using var db = CreateUserDb(); await db.Database.EnsureDeletedAsync(); }
    }
}

public static class ProfileEventSink
{
    public static readonly ConcurrentQueue<UserProfileChanged> Messages = new();
    public static void Handle(UserProfileChanged message) => Messages.Enqueue(message);
}
