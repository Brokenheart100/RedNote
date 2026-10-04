namespace RedNote.IdentityService.Features.Authentication.Register;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? DisplayName,
    string? FamilyName);

public sealed record RegisterResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    string? FamilyName,
    DateTimeOffset CreatedAtUtc);