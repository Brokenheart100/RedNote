using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ServiceDefaults;

public static class ForwardedHeadersExtensions
{
    public static TBuilder AddTrustedForwardedHeaders<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var trustAny = builder.Configuration.GetValue<bool>("Security:TrustAnyForwardedHeaders");
        if (trustAny && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("TrustAnyForwardedHeaders is only permitted in Development. Configure Security:KnownProxies or Security:KnownNetworks instead.");
        var proxies = builder.Configuration.GetSection("Security:KnownProxies").Get<string[]>() ?? [];
        var networks = builder.Configuration.GetSection("Security:KnownNetworks").Get<string[]>() ?? [];
        var proxyAddresses = proxies.Select(IPAddress.Parse).ToArray();
        var proxyNetworks = networks.Select(System.Net.IPNetwork.Parse).ToArray();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            // Preserve ASP.NET Core's loopback defaults unless the operator supplies an explicit allowlist.
            if (trustAny || proxyAddresses.Length > 0 || proxyNetworks.Length > 0)
            {
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
            }
            foreach (var address in proxyAddresses) options.KnownProxies.Add(address);
            foreach (var network in proxyNetworks) options.KnownIPNetworks.Add(network);
        });
        return builder;
    }
}
