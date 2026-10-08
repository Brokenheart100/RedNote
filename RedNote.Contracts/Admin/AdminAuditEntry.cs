using System.ComponentModel.DataAnnotations;

namespace RedNote.Contracts.Admin;

public sealed class AdminAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorUserId { get; set; }
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(30)] public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    [MaxLength(500)] public string Reason { get; set; } = "";
    [MaxLength(1000)] public string Change { get; set; } = "";
    [MaxLength(64)] public string TraceId { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
