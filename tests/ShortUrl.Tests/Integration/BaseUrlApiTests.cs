using System.Net;
using System.Net.Http.Json;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

public sealed class BaseUrlApiTests
{
    [Fact]
    public async Task Create_WithoutConfiguredBaseUrl_DerivesItFromTheRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/urls",
            new CreateShortUrlRequest("https://example.com/base-url", null, 30));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);
        Assert.Equal($"http://localhost/{created!.ShortCode}", created.ShortUrl);
    }
}
