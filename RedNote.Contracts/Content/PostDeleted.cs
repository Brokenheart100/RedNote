namespace RedNote.Contracts.Content;

public sealed record PostDeleted(
    Guid PostId,
    DateTimeOffset DeletedAtUtc,
    long Revision = 0);
