using Microsoft.AspNetCore.HttpOverrides;

namespace ShortUrl.Api.Configuration;

public static class ForwardedHeadersSetup
{
    public static ForwardedHeadersOptions CreateOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        // Render assigns proxies from a dynamic pool, so the default loopback-only
        // allow list would ignore its headers. ForwardLimit defaults to 1, which
        // consumes only the rightmost entry appended by the proxy.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        return options;
    }
}
