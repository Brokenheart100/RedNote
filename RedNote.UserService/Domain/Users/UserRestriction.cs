namespace RedNote.UserService.Domain.Users;

public sealed class UserRestriction
{
    [System.ComponentModel.DataAnnotations.Key] public Guid UserId { get; set; }
    public bool PublishingRestricted { get; set; }
    public bool CommentingRestricted { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
