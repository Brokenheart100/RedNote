using System.Net;
using System.Net.Http.Json;
using Alba;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RedNote.IdentityService.Domain.Users;
using RedNote.IdentityService.Features.Authentication.Admin;
using RedNote.IdentityService.Features.Authentication.Csrf;
using RedNote.IdentityService.Features.Authentication.Register;
using RedNote.IdentityService.Features.Authentication.SessionLogin;
using RedNote.IdentityService.Features.Authentication.SessionLogout;
using RedNote.IdentityService.Infrastructure.Persistence;
using RedNote.IdentityService.Infrastructure.Http;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Http.FluentValidation;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class IdentityCsrfTests : IAsyncLifetime
{
    private IAlbaHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddDbContext<IdentityServiceDbContext>(x => x.UseNpgsql(
            "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1").UseOpenIddict());
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>().AddEntityFrameworkStores<IdentityServiceDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.AddAntiforgery(x => { x.HeaderName = "X-CSRF-TOKEN"; x.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None; });
        builder.Services.AddWolverineHttp();
        builder.Services.AddAuthorization();
        builder.Host.UseWolverine(x =>
        {
            x.ApplicationAssembly = typeof(SessionLoginEndpoint).Assembly;
            x.UseRuntimeCompilation();
            x.UseFluentValidation();
            x.CodeGeneration.AlwaysUseServiceLocationFor<UserManager<ApplicationUser>>();
            x.CodeGeneration.AlwaysUseServiceLocationFor<SignInManager<ApplicationUser>>();
        });
        var endpoints = new HashSet<Type> { typeof(CsrfEndpoint), typeof(SessionLoginEndpoint), typeof(SessionLogoutEndpoint),
            typeof(RegisterEndpoint), typeof(AdminLoginEndpoint) };
        _host = await AlbaHost.For(builder, app =>
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapWolverineEndpoints(x =>
            {
                x.AddMiddleware(typeof(AntiforgeryResultMiddleware));
                x.CustomizeHttpEndpointDiscovery(q => q.Excludes.WithCondition("only auth endpoints", t => !endpoints.Contains(t)));
                x.UseFluentValidationProblemDetailMiddleware();
                x.UseApiVersioning(v => { v.UrlSegmentPrefix = "api/v{version}"; v.UnversionedPolicy = UnversionedPolicy.PassThrough; });
            });
        });
        _client = _host.Server.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/auth/session/login")]
    [InlineData("/api/v1/auth/session/logout")]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/api/v1/auth/admin/login")]
    public async Task MissingAndInvalidTokensAreRejectedBeforeBusinessLogic(string path)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(path)).StatusCode);
        var csrf = await Tokens();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(path, csrf.cookie, "invalid-token")).StatusCode);
        var other = await Tokens();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(path, other.cookie, csrf.token)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/session/logout", HttpStatusCode.OK)]
    [InlineData("/api/v1/auth/admin/login", HttpStatusCode.Unauthorized)]
    [InlineData("/api/v1/auth/session/login", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/auth/register", HttpStatusCode.BadRequest)]
    public async Task ValidTokenReachesTheActualEndpoint(string path, HttpStatusCode expected)
    {
        var csrf = await Tokens();
        var result = await Send(path, csrf.cookie, csrf.token);
        Assert.Equal(expected, result.StatusCode);
        if (path.EndsWith("/register", StringComparison.Ordinal) || path.EndsWith("/session/login", StringComparison.Ordinal))
            Assert.Contains("errors", await result.Content.ReadAsStringAsync());
    }

    private async Task<(string cookie, string token)> Tokens()
    {
        var result = await _client.GetAsync("/api/v1/auth/csrf");
        result.EnsureSuccessStatusCode();
        var body = await result.Content.ReadFromJsonAsync<CsrfEndpoint.CsrfResponse>();
        return (result.Headers.GetValues("Set-Cookie").Single().Split(';')[0], body!.Token);
    }

    private async Task<HttpResponseMessage> Send(string path, string? cookie = null, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { email = "", password = "", code = "" }) };
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await _client.SendAsync(request);
    }

    public async Task DisposeAsync() { _client?.Dispose(); if (_host is not null) await _host.DisposeAsync(); }
}
