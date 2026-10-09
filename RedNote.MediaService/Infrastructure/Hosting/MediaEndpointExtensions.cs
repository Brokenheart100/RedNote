using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace RedNote.MediaService.Infrastructure.Hosting;

public static class MediaEndpointExtensions
{
    public static void ConfigureMediaEndpoints(this WebApplicationBuilder builder)
    {
        // A development container still uses Aspire's HTTPS endpoints. The AppHost
        // explicitly selects the separate HTTP listeners when publishing containers.
        if (!builder.Configuration.GetValue<bool>("Media:UseContainerEndpoints")) return;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(8080, endpoint => endpoint.Protocols = HttpProtocols.Http1);
            // Cleartext gRPC requires an HTTP/2-only listener.
            options.ListenAnyIP(8081, endpoint => endpoint.Protocols = HttpProtocols.Http2);
        });
    }
}
