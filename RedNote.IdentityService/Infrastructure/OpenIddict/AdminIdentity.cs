using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using RedNote.Authentication;
using RedNote.IdentityService.Domain.Users;
using OpenIddict.Abstractions;
using RedNote.IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RedNote.IdentityService.Infrastructure.OpenIddict;

internal static class AdminIdentity
{
    public const string ClientId = "rednote-admin";
    public const string Scope = "rednote-admin";
    public const string MfaTime = "admin_mfa_at";
    public static bool FreshMfa(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirst(MfaTime)?.Value, out var value)
        && value <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        && DateTimeOffset.FromUnixTimeSeconds(value) > DateTimeOffset.UtcNow.AddHours(-8);

    public static string Version(ApplicationUser user) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.SecurityStamp ?? "")));

    public static async Task<bool> EligibleAsync(ApplicationUser user, UserManager<ApplicationUser> users) =>
        user.TwoFactorEnabled && !await users.IsLockedOutAsync(user)
        && (await users.GetRolesAsync(user)).Any(AdminAuthorization.Roles.Contains);

    public static void SetClaims(ClaimsPrincipal principal, ApplicationUser user, string mfaTime)
    {
        principal.SetClaim("admin_client", ClientId);
        principal.SetClaim("admin_version", Version(user));
        principal.SetClaim(MfaTime, mfaTime);
        principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(5));
        principal.SetRefreshTokenLifetime(TimeSpan.FromHours(8));
    }

    public static async Task ProvisionAsync(IServiceProvider provider, IConfiguration configuration)
    {
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var email = configuration["AdminBootstrap:Email"] ?? throw new InvalidOperationException("AdminBootstrap:Email is required.");
        var password = configuration["AdminBootstrap:Password"] ?? throw new InvalidOperationException("AdminBootstrap:Password is required.");
        var output = configuration["AdminBootstrap:EnrollmentFile"] ?? throw new InvalidOperationException("AdminBootstrap:EnrollmentFile is required.");
        if (File.Exists(output)) throw new InvalidOperationException("Enrollment output already exists.");
        var requested = configuration["AdminBootstrap:Roles"]?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (requested.Length == 0 || requested.Any(role => !AdminAuthorization.Roles.Contains(role)))
            throw new InvalidOperationException("Specify valid fixed admin roles.");
        var user = await users.FindByEmailAsync(email);
        if (user is not null) throw new InvalidOperationException("Provisioning only creates new administrators; it never resets an existing account.");
        var db = scope.ServiceProvider.GetRequiredService<IdentityServiceDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Administrator" };
        Ensure(await users.CreateAsync(user, password));
        foreach (var role in requested)
        {
            if (!await roles.RoleExistsAsync(role)) Ensure(await roles.CreateAsync(new IdentityRole<Guid>(role)));
            Ensure(await users.AddToRoleAsync(user, role));
        }
        Ensure(await users.ResetAuthenticatorKeyAsync(user));
        var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("Authenticator enrollment failed.");
        Ensure(await users.SetTwoFactorEnabledAsync(user, true));
        var uri = $"otpauth://totp/RedNote%20Admin:{Uri.EscapeDataString(email)}?secret={key}&issuer=RedNote%20Admin&digits=6";
        await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(new { user.Id, Email = email, AuthenticatorUri = uri }));
        await transaction.CommitAsync();
        Console.WriteLine("Administrator created. Import the protected enrollment file into your authenticator, then remove that file.");
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
