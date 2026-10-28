using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ShortUrl.Tests.Integration;

public sealed class RootEndpointTests
{
    [Fact]
    public async Task Root_WhenSwaggerEnabled_RedirectsToSwagger()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Root_WhenSwaggerDisabled_ReturnsJsonIndex()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Swagger:Enabled"] = "false"
                })));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var index = await response.Content.ReadFromJsonAsync<RootIndexResponse>();
        Assert.NotNull(index);
        Assert.Equal("Short URL API", index!.Name);
        Assert.Contains("/api/urls", index.Endpoints);
    }

    private sealed record RootIndexResponse(string Name, string Swagger, string Health, string[] Endpoints);
}
