using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RedNote.Authentication;

public static class AdminAuthorization
{
    public const string Access = "AdminAccess";
    public const string Moderate = "content.moderate";
    public const string Users = "users.restrict";
    public const string Audit = "audit.read";
    public static readonly string[] Roles = ["ContentModerator", "UserAdministrator", "AuditReader"];

    public static bool IsAdminToken(System.Security.Claims.ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true
        && principal.FindFirst("admin_client")?.Value == "rednote-admin"
        && principal.FindFirst("scope")?.Value.Split(' ').Contains("rednote-admin") == true;

    public static IServiceCollection AddAdminAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient("admin-identity");
        services.AddScoped<IAuthorizationHandler, CurrentAdminHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Access, policy => policy.RequireAuthenticatedUser().RequireAssertion(context => IsAdminToken(context.User)));
            foreach (var permission in new[] { Moderate, Users, Audit })
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new CurrentAdminRequirement(permission)));
        });
        return services;
    }

    public static string[] Permissions(IEnumerable<string> roles) => roles.SelectMany(role => role switch
    {
        "ContentModerator" => [Moderate],
        "UserAdministrator" => [Users],
        "AuditReader" => [Audit],
        _ => Array.Empty<string>()
    }).Distinct().ToArray();

    private sealed record CurrentAdminRequirement(string Permission) : IAuthorizationRequirement;

    private sealed class CurrentAdminHandler(IHttpClientFactory clients, IHttpContextAccessor accessor,
        IOptions<JwtAuthenticationOptions> jwt) : AuthorizationHandler<CurrentAdminRequirement>
    {
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CurrentAdminRequirement requirement)
        {
            var httpContext = accessor.HttpContext;
            if (httpContext is null || !IsAdminToken(context.User)) return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    new Uri(new Uri(jwt.Value.MetadataAddress), "/api/v1/auth/admin/access"));
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(httpContext.Request.Headers.Authorization.ToString());
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var response = await clients.CreateClient("admin-identity").SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) return;
                var access = await response.Content.ReadFromJsonAsync<AdminAccessResponse>(timeout.Token);
                if (access?.Permissions.Contains(requirement.Permission) == true) context.Succeed(requirement);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                context.Fail();
            }
        }
    }
}

public sealed record AdminAccessResponse(string UserId, string? Email, string[] Roles, string[] Permissions);
