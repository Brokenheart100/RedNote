using System.Net.Http.Headers;
using System.Text.Json;

namespace RedNote.AdminService.Infrastructure.Clients;

public sealed class AdminBusinessClient(IHttpClientFactory clients, ILogger<AdminBusinessClient> logger)
{
    public async Task<IResult> ForwardAsync(HttpContext context, string service, string path, JsonElement? body = null)
    {
        using var request = new HttpRequestMessage(body.HasValue ? HttpMethod.Post : HttpMethod.Get, path + context.Request.QueryString);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(context.Request.Headers.Authorization.ToString());
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var key)) request.Headers.TryAddWithoutValidation("Idempotency-Key", key.ToString());
        if (body.HasValue) request.Content = JsonContent.Create(body.Value);
        try
        {
            using var response = await clients.CreateClient(service).SendAsync(request, context.RequestAborted);
            if (response.Headers.TryGetValues("Retry-After", out var retryAfter)) context.Response.Headers.RetryAfter = retryAfter.First();
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || (body.HasValue && response.IsSuccessStatusCode)) return Results.NoContent();
            return Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted),
                response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Admin downstream {Service} unavailable", service);
            return Results.Problem(statusCode: 503, title: "Business service unavailable.");
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            return Results.Problem(statusCode: 504, title: "Business request timed out; verify its result before retrying.");
        }
    }
}
