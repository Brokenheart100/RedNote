using RedNote.Contracts.Content;

namespace RedNote.Contracts.Recommendations;

public sealed record RecommendationFeedResponse(IReadOnlyList<PostResponse> Items, string? NextCursor,
    bool HasMore, string RequestId, string Strategy);
