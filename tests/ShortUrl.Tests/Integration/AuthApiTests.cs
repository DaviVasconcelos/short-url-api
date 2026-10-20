using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ShortUrl.Application.Dtos;

namespace ShortUrl.Tests.Integration;

public sealed class AuthApiTests : IClassFixture<ApiFactory>
{
    private const string Password = "Password123!";

    private readonly ApiFactory _factory;

    public AuthApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisterThenMe_ReturnsAuthenticatedUser()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
        Assert.Equal(email.ToLowerInvariant(), auth.User.Email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Token);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var user = await me.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal(auth.User.Id, user!.Id);
        Assert.Equal(auth.User.Email, user.Email);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var first = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));

        var second = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email.ToUpperInvariant(), Password));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_WithInvalidPassword_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(UniqueEmail(), "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, "WrongPassword123!"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithInvalidToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string UniqueEmail() =>
        $"user-{Guid.NewGuid():N}@example.com";
}
