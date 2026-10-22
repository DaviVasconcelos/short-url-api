using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

public sealed class UrlsOwnershipApiTests : IClassFixture<ApiFactory>
{
    private const string Password = "Password123!";

    private readonly ApiFactory _factory;

    public UrlsOwnershipApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Owner_CanDeleteOwnedLink()
    {
        var (client, _) = await RegisterAsync();
        var created = await CreateAsync(client);

        var delete = await client.DeleteAsync($"/api/urls/{created.ShortCode}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/urls/{created.ShortCode}")).StatusCode);
    }

    [Fact]
    public async Task NonOwner_CannotDeleteOwnedLink()
    {
        var (ownerClient, _) = await RegisterAsync();
        var created = await CreateAsync(ownerClient);
        var (otherClient, _) = await RegisterAsync();

        var delete = await otherClient.DeleteAsync($"/api/urls/{created.ShortCode}");

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await ownerClient.GetAsync($"/api/urls/{created.ShortCode}")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUser_CannotDeleteAnonymousLink()
    {
        var anonymousClient = CreateClient();
        var created = await CreateAsync(anonymousClient);
        var (authenticatedClient, _) = await RegisterAsync();

        var delete = await authenticatedClient.DeleteAsync($"/api/urls/{created.ShortCode}");

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await anonymousClient.GetAsync($"/api/urls/{created.ShortCode}")).StatusCode);
    }

    [Fact]
    public async Task DeleteWithoutToken_ReturnsUnauthorized()
    {
        var anonymousClient = CreateClient();
        var created = await CreateAsync(anonymousClient);

        var delete = await anonymousClient.DeleteAsync($"/api/urls/{created.ShortCode}");

        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await anonymousClient.GetAsync($"/api/urls/{created.ShortCode}")).StatusCode);
    }

    [Fact]
    public async Task CreateWithInvalidToken_ReturnsUnauthorized()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.PostAsJsonAsync(
            "/api/urls",
            new CreateShortUrlRequest("https://example.com/owned", null, 30));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithInvalidTokenAndMalformedBody_ReturnsUnauthorized()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/urls", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWithInvalidToken_ReturnsUnauthorized()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.DeleteAsync("/api/urls/anything");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMissingCodeWithToken_ReturnsNotFound()
    {
        var (client, _) = await RegisterAsync();

        var response = await client.DeleteAsync("/api/urls/doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, AuthResponse Auth)> RegisterAsync()
    {
        var client = CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

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

    private static async Task<ShortUrlResponse> CreateAsync(
        HttpClient client,
        string url = "https://example.com/owned")
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
