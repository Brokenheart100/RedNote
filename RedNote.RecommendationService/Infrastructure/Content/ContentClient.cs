using System.Net.Http.Headers;
using System.Net.Http.Json;
using RedNote.Contracts.Content;

namespace RedNote.RecommendationService.Infrastructure.Content;

public sealed class ContentClient(HttpClient http)
{
    public async Task<PostResponse[]> Batch(Guid[] ids, string? authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/posts/batch")
        { Content = JsonContent.Create(new { postIds = ids }) };
        if (!string.IsNullOrEmpty(authorization)) request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new BadHttpRequestException("Content details are temporarily unavailable.", 503);
        return await response.Content.ReadFromJsonAsync<PostResponse[]>(ct) ?? [];
    }
}
