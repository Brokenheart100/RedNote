using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using RedNote.UserService.Infrastructure.Persistence;
using Wolverine.Http;

namespace RedNote.UserService.Features.Users.GetMyFollowingIds;

[ApiVersion("1.0")]
[Authorize]
public static class GetMyFollowingIdsEndpoint
{
    [WolverineGet("/users/me/following/ids")]
    public static async Task<IResult> Get(
        ClaimsPrincipal principal,
        UserServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var subject =
            principal.FindFirst("sub")
                ?.Value;

        if (!Guid.TryParse(
                subject,
                out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var userIds = await FollowingIdsQuery.LoadAsync(
            dbContext, currentUserId, cancellationToken);

        return Results.Ok(
            new FollowingIdsResponse(
                userIds));
    }
}

public sealed record FollowingIdsResponse(
    IReadOnlyList<Guid> UserIds);
