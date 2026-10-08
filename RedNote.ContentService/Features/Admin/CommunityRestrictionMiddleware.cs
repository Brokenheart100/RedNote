using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RedNote.Authentication;

namespace RedNote.ContentService.Features.Admin;

internal sealed class CommunityRestrictionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IHttpClientFactory clients, IOptions<JwtAuthenticationOptions> jwt)
    {
        // ASP.NET routing accepts case differences and a trailing slash.
        // Restrictions must cover the same paths as the endpoints themselves.
        var path = (context.Request.Path.Value ?? "").TrimEnd('/');
        var publishing = string.Equals(path, "/api/v1/posts", StringComparison.OrdinalIgnoreCase);
        var commenting = path.StartsWith("/api/v1/posts/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/comments", StringComparison.OrdinalIgnoreCase);
        if (HttpMethods.IsPost(context.Request.Method) && (publishing || commenting) && context.User.Identity?.IsAuthenticated == true)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(jwt.Value.MetadataAddress), "/api/v1/users/me/restrictions"));
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(context.Request.Headers.Authorization.ToString());
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var response = await clients.CreateClient("community-restrictions").SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) { context.Response.StatusCode = 503; return; }
                var restriction = await response.Content.ReadFromJsonAsync<RestrictionState>(timeout.Token);
                if (restriction is null) { context.Response.StatusCode = 503; return; }
                if ((publishing && restriction.PublishingRestricted) || (commenting && restriction.CommentingRestricted))
                { await Results.Problem(statusCode: 403, title: "This community action is restricted.").ExecuteAsync(context); return; }
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            { context.Response.StatusCode = 503; return; }
        }
        await next(context);
    }

    private sealed record RestrictionState(bool PublishingRestricted, bool CommentingRestricted);
}
