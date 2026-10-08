using System.Security.Claims;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RedNote.RecommendationService.Features.Feed;
using RedNote.RecommendationService.Features.Synchronization;
using RedNote.RecommendationService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace RedNote.RecommendationService.Features.Feedback;

public sealed class RecommendationQuery
{
    [FromQuery(Name = "pageSize")] public int PageSize { get; set; } = 20;
    [FromQuery(Name = "cursor")] public string? Cursor { get; set; }
}
public sealed class RecommendationQueryValidator : AbstractValidator<RecommendationQuery>
{
    public RecommendationQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50).OverridePropertyName("pageSize");
        RuleFor(x => x.Cursor).MaximumLength(80).OverridePropertyName("cursor");
    }
}
public sealed record RecommendationFeedbackInput(Guid PostId, string Type);
public sealed record RecommendationFeedbackRequest(Guid RequestId, IReadOnlyList<RecommendationFeedbackInput> Items);
public sealed class RecommendationFeedbackRequestValidator : AbstractValidator<RecommendationFeedbackRequest>
{
    public RecommendationFeedbackRequestValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Items).NotNull().Must(x => x is { Count: > 0 and <= 50 });
        RuleForEach(x => x.Items).NotNull().ChildRules(item =>
        {
            item.RuleFor(x => x.PostId).NotEmpty();
            item.RuleFor(x => x.Type).Must(x => x is "read" or "click");
        });
    }
}
[ApiVersion("1.0")]
public static class RecommendationEndpoints
{
    [AllowAnonymous, WolverineGet("/posts/recommended")]
    public static async Task<IResult> Get([AsParameters] RecommendationQuery query, ClaimsPrincipal principal, HttpContext context,
        [FromServices] RecommendationFeed feed, CancellationToken ct)
    {
        Guid? user = Guid.TryParse(principal.FindFirst("sub")?.Value, out var id) ? id : null;
        var authorization = principal.Identity?.IsAuthenticated == true ? context.Request.Headers.Authorization.ToString() : null;
        return Results.Ok(await feed.Get(user, query.PageSize, query.Cursor, authorization, ct));
    }

    [Authorize, WolverinePost("/posts/recommendations/feedback"), Transactional]
    public static async Task<IResult> Post(RecommendationFeedbackRequest request, ClaimsPrincipal principal,
        [FromServices] RecommendationFeed feed, [FromServices] RecommendationDbContext db, [FromServices] IMessageBus bus, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var user)) return Results.Unauthorized();
        var snapshot = await feed.GetSnapshot(request.RequestId, ct);
        if (snapshot is null) return Results.StatusCode(410);
        if (snapshot.Owner != RecommendationFeed.Owner(user)) return Results.Forbid();
        var inputs = request.Items.Distinct().ToArray();
        foreach (var input in inputs)
            if (!await feed.WasDelivered(request.RequestId, input.PostId, ct)) return Results.BadRequest();
        var at = DateTimeOffset.UtcNow;
        foreach (var input in inputs)
        {
            if (await db.Receipts.FindAsync([request.RequestId, user, input.PostId, input.Type], ct) is not null) continue;
            db.Receipts.Add(new() { RequestId = request.RequestId, UserId = user, PostId = input.PostId, Type = input.Type, RecordedAtUtc = at });
            var state = await db.Feedback.FindAsync([input.PostId, user, input.Type], ct);
            if (state is null) { state = new() { PostId = input.PostId, UserId = user, Type = input.Type }; db.Feedback.Add(state); }
            state.IsActive = true; state.OccurredAtUtc = at; state.Version++;
            await bus.PublishAsync(new SyncRecommendationItem(input.PostId));
        }
        return Results.NoContent();
    }
}
