namespace RedNote.UserService.Features.Users.UpdateMe;

public sealed record UpdateMeRequest(
    string? Nickname,
    string? AvatarUrl,
    string? Bio);

public sealed record UpdateMeResponse(
    Guid UserId,
    string? Nickname,
    string? AvatarUrl,
    string? Bio,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);