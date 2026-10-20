using System.Net;
using System.Net.Http.Json;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

public sealed class AuthRateLimitTests
{
    [Fact]
    public async Task Login_AfterTenAttempts_ReturnsTooManyRequests()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest($"missing-{attempt}@example.com", "Password123!"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var rejected = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("missing-final@example.com", "Password123!"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }
}
