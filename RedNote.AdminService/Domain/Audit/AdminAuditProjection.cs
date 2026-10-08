namespace RedNote.AdminService.Domain.Audit;

public sealed class AdminAuditProjection
{
    public Guid Id { get; set; }
    public string Source { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    public string Reason { get; set; } = "";
    public string Change { get; set; } = "";
    public string TraceId { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
