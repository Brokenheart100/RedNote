using RedNote.AdminService.Domain.Audit;
using RedNote.AdminService.Infrastructure.Persistence;
using RedNote.Contracts.Admin;
using Wolverine.Attributes;

namespace RedNote.AdminService.Features.Audit;

public static class AdminAuditRecordedHandler
{
    [Transactional]
    public static async Task Handle(AdminAuditRecorded message, AdminServiceDbContext db, CancellationToken cancellationToken)
    {
        if (message.Source is not ("content" or "user")) throw new ArgumentException("Unknown audit source.");
        await db.InsertAuditIfAbsentAsync(new AdminAuditProjection
        {
            Id = message.Id,
            Source = message.Source,
            ActorUserId = message.ActorUserId,
            Action = message.Action,
            TargetType = message.TargetType,
            TargetId = message.TargetId,
            Reason = message.Reason,
            Change = message.Change,
            TraceId = message.TraceId,
            CreatedAtUtc = message.CreatedAtUtc
        }, cancellationToken);
    }
}
