using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

/// <summary>
/// Integration tests against a real PostgreSQL instance. They prove that the PostgreSQL
/// migrations apply on startup and that the Npgsql code paths (unique index, click
/// increment through ExecuteUpdateAsync, ownership filters) behave like SQLite.
/// </summary>
public sealed class PostgresApiTests : IClassFixture<PostgresApiFactory>
{
    private const string Password = "Password123!";

    private readonly PostgresApiFactory _factory;

    public PostgresApiTests(PostgresApiFactory factory)
    {
        _factory = factory;
    }

    [PostgresFact]
    public async Task Health_IsReadyAgainstPostgres()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [PostgresFact]
    public async Task CreateThenRedirect_TracksTheClick()
    {
        using var client = CreateClient();
        var originalUrl = $"https://example.com/postgres/{Guid.NewGuid():N}";

        var created = await CreateAsync(client, originalUrl);

        var redirect = await client.GetAsync($"/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal(originalUrl, redirect.Headers.Location?.ToString());

        var stats = await client.GetFromJsonAsync<ShortUrlStatsResponse>(
            $"/api/urls/{created.ShortCode}");
        Assert.NotNull(stats);
        Assert.Equal(originalUrl, stats!.OriginalUrl);
        Assert.True(stats.ClickCount >= 1);
    }

    [PostgresFact]
    public async Task OwnedLink_CanBeListedAndDeleted()
    {
        var (client, _) = await RegisterAsync();
        var created = await CreateAsync(client, "https://example.com/postgres-owned");

        var list = await client.GetFromJsonAsync<PagedShortUrlResponse>("/api/urls?page=1&pageSize=50");
        Assert.NotNull(list);
        Assert.Contains(list!.Items, item => item.ShortCode == created.ShortCode);

        var delete = await client.DeleteAsync($"/api/urls/{created.ShortCode}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/urls/{created.ShortCode}")).StatusCode);
    }

    [PostgresFact]
    public async Task Stats_ExposesTheStoredRow()
    {
        using var client = CreateClient();

        var created = await CreateAsync(client, "https://example.com/postgres-stats", expiresInDays: 30);

        var stats = await client.GetFromJsonAsync<ShortUrlStatsResponse>(
            $"/api/urls/{created.ShortCode}");

        Assert.NotNull(stats);
        Assert.Equal(created.ShortCode, stats!.ShortCode);
        Assert.Equal(0, stats.ClickCount);
        Assert.NotNull(stats.ExpiresAt);
        Assert.True(stats.CreatedAt <= DateTime.UtcNow.AddMinutes(1));
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<ShortUrlResponse> CreateAsync(
        HttpClient client,
        string url,
        int? expiresInDays = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/urls",
            new CreateShortUrlRequest(url, null, expiresInDays));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.ShortCode));

        return created;
    }

    private async Task<(HttpClient Client, AuthResponse Auth)> RegisterAsync()
    {
        var client = CreateClient();
        var email = $"postgres-{Guid.NewGuid():N}@example.com";

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
}
