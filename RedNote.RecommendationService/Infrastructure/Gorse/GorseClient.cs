using System.Net.Http.Json;

namespace RedNote.RecommendationService.Infrastructure.Gorse;

// Deliberately limited to the official REST operations this service requires.
// POST feedback accumulates Value; PUT writes the absolute binary state.
public sealed record GorseItem(string ItemId, bool IsHidden, DateTimeOffset Timestamp, object Labels);
public sealed record GorseFeedback(string FeedbackType, string UserId, string ItemId, DateTimeOffset Timestamp, float Value = 1);
public sealed record GorseFeedbackPage(string Cursor, GorseFeedback[] Feedback);
public sealed class GorseClient(HttpClient http)
{
    public async Task<string[]> Recommend(Guid user, CancellationToken ct) =>
        await http.GetFromJsonAsync<string[]>($"api/recommend/{user}?n=500", ct) ?? [];
    public async Task Upsert(GorseItem item, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/item", item, ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task Delete(Guid post, CancellationToken ct)
    {
        using var response = await http.DeleteAsync($"api/item/{post}", ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task SetFeedback(GorseFeedback[] feedback, CancellationToken ct)
    {
        if (feedback.Length == 0) return;
        using var response = await http.PutAsJsonAsync("api/feedback", feedback, ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task DeleteFeedback(Guid post, Guid user, string type, CancellationToken ct)
    {
        using var response = await http.DeleteAsync($"api/feedback/{Uri.EscapeDataString(type)}/{user}/{post}", ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task<GorseFeedbackPage> ListFeedback(string cursor, CancellationToken ct) =>
        await http.GetFromJsonAsync<GorseFeedbackPage>($"api/feedback?n=100&cursor={Uri.EscapeDataString(cursor)}", ct)
            ?? new("", []);
}
