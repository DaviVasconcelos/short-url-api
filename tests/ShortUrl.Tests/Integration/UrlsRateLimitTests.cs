using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

/// <summary>
/// The rate limiter partitions by user id once the request is authenticated, so a caller
/// cannot reset the counter by changing IP. Limits are overridden per test through
/// <c>UseSetting</c>, because <c>Program.cs</c> reads them before configuration callbacks run.
/// </summary>
public sealed class UrlsRateLimitTests
{
    private const string Password = "Password123!";

    [Fact]
    public async Task Create_IsLimitedPerUser()
    {
        using var baseFactory = new ApiFactory();
        using var factory = WithLimits(baseFactory, createPerMinute: 2);
        var alice = await RegisterAsync(factory);
        var bob = await RegisterAsync(factory);

        Assert.Equal(HttpStatusCode.Created, (await PostCreateAsync(alice)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostCreateAsync(alice)).StatusCode);

        var limited = await PostCreateAsync(alice);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);

        // Bob has his own bucket: Alice exhausting hers must not affect him.
        Assert.Equal(HttpStatusCode.Created, (await PostCreateAsync(bob)).StatusCode);

        // Anonymous callers fall back to the IP bucket and are still limited.
        var anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Created, (await PostCreateAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostCreateAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostCreateAsync(anonymous)).StatusCode);
    }

    [Fact]
    public async Task List_IsLimitedPerUser()
    {
        using var baseFactory = new ApiFactory();
        using var factory = WithLimits(baseFactory, listPerMinute: 2);
        var client = await RegisterAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/urls")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/urls")).StatusCode);

        var limited = await client.GetAsync("/api/urls");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task Delete_IsLimitedPerUser()
    {
        using var baseFactory = new ApiFactory();
        using var factory = WithLimits(baseFactory, deletePerMinute: 2);
        var client = await RegisterAsync(factory);
        var first = await CreateAsync(client);
        var second = await CreateAsync(client);
        var third = await CreateAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/urls/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/urls/{second}")).StatusCode);

        var limited = await client.DeleteAsync($"/api/urls/{third}");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    private static WebApplicationFactory<Program> WithLimits(
        ApiFactory baseFactory,
        int? createPerMinute = null,
        int? listPerMinute = null,
        int? deletePerMinute = null) =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            if (createPerMinute is { } create)
            {
                builder.UseSetting(
                    "RateLimiting:CreatePerMinute",
                    create.ToString(CultureInfo.InvariantCulture));
            }

            if (listPerMinute is { } list)
            {
                builder.UseSetting(
                    "RateLimiting:ListPerMinute",
                    list.ToString(CultureInfo.InvariantCulture));
            }

            if (deletePerMinute is { } delete)
            {
                builder.UseSetting(
                    "RateLimiting:DeletePerMinute",
                    delete.ToString(CultureInfo.InvariantCulture));
            }
        });

    private static async Task<HttpClient> RegisterAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"ratelimit-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.Token);

        return client;
    }

    private static Task<HttpResponseMessage> PostCreateAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "/api/urls",
            new CreateShortUrlRequest($"https://example.com/{Guid.NewGuid():N}", null, 30));

    private static async Task<string> CreateAsync(HttpClient client)
    {
        var response = await PostCreateAsync(client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        return created!.ShortCode;
    }
}
