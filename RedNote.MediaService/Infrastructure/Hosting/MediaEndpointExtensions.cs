using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace RedNote.MediaService.Infrastructure.Hosting;

public static class MediaEndpointExtensions
{
    public static void ConfigureMediaEndpoints(this WebApplicationBuilder builder)
    {
        // The .NET container image supplies this flag. Local Aspire keeps its HTTPS launch endpoint.
        if (!builder.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER")) return;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(8080, endpoint => endpoint.Protocols = HttpProtocols.Http1);
            // Cleartext gRPC requires an HTTP/2-only listener.
            options.ListenAnyIP(8081, endpoint => endpoint.Protocols = HttpProtocols.Http2);
        });
    }
}
