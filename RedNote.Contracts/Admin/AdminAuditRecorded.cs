namespace RedNote.Contracts.Admin;

// Immutable integration contract. Persistence models belong to their own services.
public sealed record AdminAuditRecorded(Guid Id, string Source, Guid ActorUserId, string Action,
    string TargetType, Guid TargetId, string Reason, string Change, string TraceId, DateTimeOffset CreatedAtUtc);
