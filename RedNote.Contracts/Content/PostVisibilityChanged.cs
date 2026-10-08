namespace RedNote.Contracts.Content;

public sealed record PostVisibilityChanged(Guid PostId, bool IsHidden, long Revision);
