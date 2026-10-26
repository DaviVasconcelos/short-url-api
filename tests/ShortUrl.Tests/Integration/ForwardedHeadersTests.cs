using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShortUrl.Api.Configuration;

namespace ShortUrl.Tests.Integration;

public sealed class ForwardedHeadersTests
{
    [Fact]
    public async Task ApplyForwarders_UsesForwardedClientIpAndScheme()
    {
        var context = CreateContext(IPAddress.Parse("10.0.0.5"));
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await CreateMiddleware().Invoke(context);

        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task ApplyForwarders_WithMultipleIps_UsesOnlyTheRightmostEntry()
    {
        var context = CreateContext(IPAddress.Parse("10.0.0.5"));
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.1, 203.0.113.9";

        await CreateMiddleware().Invoke(context);

        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
    }

    private static DefaultHttpContext CreateContext(IPAddress remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;
        return context;
    }

    private static ForwardedHeadersMiddleware CreateMiddleware() =>
        new(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(ForwardedHeadersSetup.CreateOptions()));
}
