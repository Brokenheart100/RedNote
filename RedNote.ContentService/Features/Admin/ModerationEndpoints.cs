using Microsoft.AspNetCore.Authorization;
using RedNote.Authentication;
using RedNote.Contracts.Admin;
using RedNote.ContentService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.ContentService.Features.Admin;

[Authorize(Policy = AdminAuthorization.Moderate)]
public static class ModerationEndpoints
{
    [WolverinePost("/internal/admin/posts/{id:guid}/{action}")]
    [Transactional, DeduplicatedWithResponse(DeduplicationScope.User | DeduplicationScope.Endpoint)]
    public static async Task<AdminOperationResult> Post(Guid id, string action, ModerationRequest request, HttpContext context,
        ContentServiceDbContext db, IMessageBus bus, CancellationToken ct)
    {
        var result = await AdminContentEndpoints.ModeratePost(id, action, request, context.User, db, bus, ct);
        RejectFailure(result);
        return new AdminOperationResult(id, $"post.{action}");
    }

    [WolverinePost("/internal/admin/comments/{id:guid}/{action}")]
    [Transactional, DeduplicatedWithResponse(DeduplicationScope.User | DeduplicationScope.Endpoint)]
    public static async Task<AdminOperationResult> Comment(Guid id, string action, ModerationRequest request, HttpContext context,
        ContentServiceDbContext db, IMessageBus bus, CancellationToken ct)
    {
        var result = await AdminContentEndpoints.ModerateComment(id, action, request, context.User, db, bus, ct);
        RejectFailure(result);
        return new AdminOperationResult(id, $"comment.{action}");
    }

    private static void RejectFailure(IResult result)
    {
        if (result is IStatusCodeHttpResult { StatusCode: >= 400 } error)
            throw new BadHttpRequestException("Administrative operation rejected.", error.StatusCode.Value);
    }
}
