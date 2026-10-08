using OpenIddict.Abstractions;

namespace RedNote.IdentityService.Infrastructure.OpenIddict;

internal sealed class OpenIddictSeeder(
    IServiceProvider serviceProvider,
    IConfiguration configuration)
{
    private const string ClientId =
        "rednote-web";

    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        await SeedAdminAsync(cancellationToken);
        await using var scope =
            serviceProvider.CreateAsyncScope();

        var applicationManager =
            scope.ServiceProvider
                .GetRequiredService<
                    IOpenIddictApplicationManager>();

        var redirectUri =
            GetRequiredAbsoluteUri(
                "OpenIddict:Clients:RedNoteWeb:RedirectUri");

        var postLogoutRedirectUri =
            GetRequiredAbsoluteUri(
                "OpenIddict:Clients:RedNoteWeb:PostLogoutRedirectUri");

        var application =
            await applicationManager.FindByClientIdAsync(
                ClientId,
                cancellationToken);

        var descriptor =
            CreateDescriptor(
                redirectUri,
                postLogoutRedirectUri);

        if (application is null)
        {
            await applicationManager.CreateAsync(
                descriptor,
                cancellationToken);

            return;
        }

        await applicationManager.UpdateAsync(
            application,
            descriptor,
            cancellationToken);
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = CreateDescriptor(GetRequiredAbsoluteUri("OpenIddict:Clients:RedNoteAdmin:RedirectUri"),
            GetRequiredAbsoluteUri("OpenIddict:Clients:RedNoteAdmin:PostLogoutRedirectUri"));
        descriptor.ClientId = AdminIdentity.ClientId;
        descriptor.DisplayName = "RedNote Administration";
        descriptor.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        descriptor.ClientSecret = configuration["OpenIddict:Clients:RedNoteAdmin:ClientSecret"]
            ?? throw new InvalidOperationException("Configure the admin OIDC client secret.");
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + AdminIdentity.Scope);
        var application = await manager.FindByClientIdAsync(AdminIdentity.ClientId, cancellationToken);
        if (application is null) await manager.CreateAsync(descriptor, cancellationToken);
        else await manager.UpdateAsync(application, descriptor, cancellationToken);
    }

    private static OpenIddictApplicationDescriptor
        CreateDescriptor(
            Uri redirectUri,
            Uri postLogoutRedirectUri)
    {
        var descriptor =
            new OpenIddictApplicationDescriptor
            {
                ClientId =
                    ClientId,

                DisplayName =
                    "RedNote Web",

                ClientType =
                    OpenIddictConstants
                        .ClientTypes
                        .Public,

                ConsentType =
                    OpenIddictConstants
                        .ConsentTypes
                        .Implicit
            };

        descriptor.RedirectUris.Add(
            redirectUri);

        descriptor.PostLogoutRedirectUris.Add(
            postLogoutRedirectUri);

        descriptor.Permissions.UnionWith(
        [
            OpenIddictConstants.Permissions
                .Endpoints.Authorization,

            OpenIddictConstants.Permissions
                .Endpoints.Token,

            OpenIddictConstants.Permissions
                .Endpoints.EndSession,

            OpenIddictConstants.Permissions
                .GrantTypes.AuthorizationCode,

            OpenIddictConstants.Permissions
                .GrantTypes.RefreshToken,

            OpenIddictConstants.Permissions
                .ResponseTypes.Code,

            OpenIddictConstants.Permissions
                .Scopes.Email,

            OpenIddictConstants.Permissions
                .Scopes.Profile,

            OpenIddictConstants.Permissions
                .Prefixes.Scope
                + "rednote-api"
        ]);

        descriptor.Requirements.Add(
            OpenIddictConstants.Requirements
                .Features
                .ProofKeyForCodeExchange);

        return descriptor;
    }

    private Uri GetRequiredAbsoluteUri(
        string configurationKey)
    {
        var value =
            configuration[configurationKey];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuration '{configurationKey}' is required.");
        }

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
        {
            throw new InvalidOperationException(
                $"Configuration '{configurationKey}' " +
                $"must be an absolute URI. Value: '{value}'.");
        }

        return uri;
    }
}
