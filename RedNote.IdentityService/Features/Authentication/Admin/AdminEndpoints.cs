using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using RedNote.Authentication;
using RedNote.IdentityService.Domain.Users;
using RedNote.IdentityService.Infrastructure.OpenIddict;

namespace RedNote.IdentityService.Features.Authentication.Admin;

internal static class AdminEndpoints
{
    public static void MapAdminIdentityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/auth/admin/access", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(principal.FindFirst("sub")?.Value ?? "");
            if (user is null || !await AdminIdentity.EligibleAsync(user, users) || !AdminIdentity.FreshMfa(principal)
                || principal.FindFirst("admin_version")?.Value != AdminIdentity.Version(user)) return Results.Forbid(authenticationSchemes: [JwtBearerDefaults.AuthenticationScheme]);
            var roles = (await users.GetRolesAsync(user)).Where(AdminAuthorization.Roles.Contains).ToArray();
            return Results.Ok(new AdminAccessResponse(user.Id.ToString(), user.Email, roles, AdminAuthorization.Permissions(roles)));
        }).RequireAuthorization(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser().RequireAssertion(context => AdminAuthorization.IsAdminToken(context.User)).Build());
    }
}
