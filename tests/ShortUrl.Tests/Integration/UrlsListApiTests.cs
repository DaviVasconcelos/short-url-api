using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

public sealed class UrlsListApiTests : IClassFixture<ApiFactory>
{
    private const string Password = "Password123!";

    private readonly ApiFactory _factory;

    public UrlsListApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListWithoutToken_ReturnsUnauthorized()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/urls");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyLinksOwnedByAuthenticatedUser()
    {
        var (ownerClient, _) = await RegisterAsync("owner");
        var first = await CreateAsync(ownerClient, "https://example.com/owner-1");
        var second = await CreateAsync(ownerClient, "https://example.com/owner-2");

        var (otherClient, _) = await RegisterAsync("other");
        var other = await CreateAsync(otherClient, "https://example.com/other");

        var anonymousClient = CreateClient();
        var anonymous = await CreateAsync(anonymousClient, "https://example.com/anonymous");

        var response = await ownerClient.GetAsync("/api/urls?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedShortUrlResponse>();
        Assert.NotNull(page);
        Assert.Equal(1, page!.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.TotalPages);

        var returnedCodes = page.Items.Select(x => x.ShortCode).OrderBy(x => x).ToArray();
        var expectedCodes = new[] { first.ShortCode, second.ShortCode }.OrderBy(x => x).ToArray();
        Assert.Equal(expectedCodes, returnedCodes);
        Assert.DoesNotContain(page.Items, x => x.ShortCode == other.ShortCode);
        Assert.DoesNotContain(page.Items, x => x.ShortCode == anonymous.ShortCode);
    }

    [Fact]
    public async Task List_RespectsPageAndPageSize()
    {
        var (client, _) = await RegisterAsync("pager");

        for (var i = 0; i < 3; i++)
            await CreateAsync(client, $"https://example.com/paged/{i}");

        var response = await client.GetAsync("/api/urls?page=2&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedShortUrlResponse>();
        Assert.NotNull(page);
        Assert.Equal(2, page!.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_WithInvalidPagination_ReturnsBadRequest()
    {
        var (client, _) = await RegisterAsync("invalid-pagination");

        foreach (var query in new[] { "page=0", "page=-1", "pageSize=0", "pageSize=101" })
        {
            var response = await client.GetAsync($"/api/urls?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, AuthResponse Auth)> RegisterAsync(string prefix)
    {
        var client = CreateClient();
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth);
    }

    private static async Task<ShortUrlResponse> CreateAsync(HttpClient client, string url)
    {
        var response = await client.PostAsJsonAsync(
            "/api/urls",
            new CreateShortUrlRequest(url, null, 30));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        return created!;
    }
}
