using System.Net;

namespace ShortUrl.Tests.Integration;

public sealed class HealthApiTests
{
    [Fact]
    public async Task Live_ReturnsHealthyWithoutDatabaseCheck()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsHealthyWhenDatabaseIsReachable()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
