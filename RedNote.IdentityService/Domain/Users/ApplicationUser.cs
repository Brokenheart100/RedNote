using Microsoft.AspNetCore.Identity;

namespace RedNote.IdentityService.Domain.Users;

public sealed class ApplicationUser : IdentityUser<Guid>
{
  public string? DisplayName { get; set; }
  public string? FamilyName { get; set; }
  public string? FriendName { get; set; }
  public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}