using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

public sealed class UrlsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public UrlsApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostThenRedirect_IncrementsClickCount()
    {
        var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/urls",
            new CreateShortUrlRequest("https://example.com/article", null, 30));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        var redirect = await client.GetAsync($"/{created!.ShortCode}");
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal("https://example.com/article", redirect.Headers.Location!.ToString());

        var stats = await client.GetFromJsonAsync<ShortUrlStatsResponse>($"/api/urls/{created.ShortCode}");
        Assert.NotNull(stats);
        Assert.Equal(1L, stats!.ClickCount);
    }

    [Fact]
    public async Task Redirect_WhenUnknownCode_ReturnsNotFound()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/nosuchcode");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithExpiryBeyondCap_ReturnsBadRequest()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/urls",
            new CreateShortUrlRequest("https://example.com/too-long", null, int.MaxValue));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Invalid expiration", problem!.Title);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
