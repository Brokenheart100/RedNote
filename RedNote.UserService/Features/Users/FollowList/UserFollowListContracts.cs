namespace RedNote.UserService.Features.Users.FollowList;

public sealed record UserFollowListItem(
    Guid UserId,
    string? Nickname,
    string? AvatarUrl,
    string? Bio);

public sealed record UserFollowListResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<UserFollowListItem> Items);