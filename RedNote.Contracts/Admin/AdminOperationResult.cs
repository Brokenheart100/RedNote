namespace RedNote.Contracts.Admin;

public sealed record AdminOperationResult(Guid TargetId, string Action);
