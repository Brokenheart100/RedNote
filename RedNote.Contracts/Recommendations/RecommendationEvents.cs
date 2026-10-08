namespace RedNote.Contracts.Recommendations;

public sealed record RecommendationItemStateChanged(Guid PostId, Guid AuthorUserId, string[] Tags,
    DateTimeOffset CreatedAtUtc, bool IsPublished, bool IsHidden, long Revision);
public sealed record RecommendationPreferenceStateChanged(Guid PostId, Guid UserId, string Kind,
    bool IsActive, long Revision, DateTimeOffset OccurredAtUtc);
public sealed record ExportRecommendationCatalog(Guid RunId, Guid? AfterId = null);
public sealed record RecommendationCatalogExported(Guid RunId, Guid? AfterId, bool Complete);
public sealed record RecommendationPreferenceIdentity(Guid PostId, Guid UserId, string Kind);
public sealed record ReconcileRecommendationPreferences(RecommendationPreferenceIdentity[] Items);
