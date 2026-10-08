using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using RedNote.Authentication;
using RedNote.AdminService.Infrastructure.Clients;
using Wolverine.Http;

namespace RedNote.AdminService.Features.Management;

public static class ManagementEndpoints
{
    [WolverineGet("/api/v1/admin/posts"), Authorize(Policy = AdminAuthorization.Moderate)]
    public static Task<IResult> Posts(HttpContext context, AdminBusinessClient client) => client.ForwardAsync(context, "content-service", "/internal/admin/posts");
    [WolverineGet("/api/v1/admin/posts/{id:guid}"), Authorize(Policy = AdminAuthorization.Moderate)]
    public static Task<IResult> Post(Guid id, HttpContext context, AdminBusinessClient client) => client.ForwardAsync(context, "content-service", $"/internal/admin/posts/{id}");
    [WolverineGet("/api/v1/admin/comments"), Authorize(Policy = AdminAuthorization.Moderate)]
    public static Task<IResult> Comments(HttpContext context, AdminBusinessClient client) => client.ForwardAsync(context, "content-service", "/internal/admin/comments");
    [WolverinePost("/api/v1/admin/posts/{id:guid}/{action}"), Authorize(Policy = AdminAuthorization.Moderate)]
    public static Task<IResult> ModeratePost(Guid id, string action, JsonElement body, HttpContext context, AdminBusinessClient client) =>
        action is "hide" or "restore" ? client.ForwardAsync(context, "content-service", $"/internal/admin/posts/{id}/{action}", body) : Task.FromResult<IResult>(Results.NotFound());
    [WolverinePost("/api/v1/admin/comments/{id:guid}/{action}"), Authorize(Policy = AdminAuthorization.Moderate)]
    public static Task<IResult> ModerateComment(Guid id, string action, JsonElement body, HttpContext context, AdminBusinessClient client) =>
        action is "hide" or "restore" ? client.ForwardAsync(context, "content-service", $"/internal/admin/comments/{id}/{action}", body) : Task.FromResult<IResult>(Results.NotFound());
    [WolverineGet("/api/v1/admin/users"), Authorize(Policy = AdminAuthorization.Users)]
    public static Task<IResult> Users(HttpContext context, AdminBusinessClient client) => client.ForwardAsync(context, "user-service", "/internal/admin/users");
    [WolverinePost("/api/v1/admin/users/{id:guid}/restrictions"), Authorize(Policy = AdminAuthorization.Users)]
    public static Task<IResult> Restrict(Guid id, JsonElement body, HttpContext context, AdminBusinessClient client) =>
        client.ForwardAsync(context, "user-service", $"/internal/admin/users/{id}/restrictions", body);
}
