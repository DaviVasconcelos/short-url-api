using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using ShortUrl.Application.Dtos;
using ShortUrl.Application.Options;
using ShortUrl.Application.Services;
using ShortUrl.Tests.Support;
using Domain = ShortUrl.Domain;

namespace ShortUrl.Tests.Services;

public sealed class AuthServiceTests
{
    private const string TestKey = "test-only-jwt-signing-key-at-least-32-bytes-long";
    private const string Password = "CorrectHorseBatteryStaple";

    [Fact]
    public async Task RegisterAsync_NormalizesEmailAndHashesPassword()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);

        var result = await service.RegisterAsync(
            new RegisterRequest("  User@Example.COM  ", Password));

        Assert.Equal(RegisterStatus.Created, result.Status);
        Assert.NotNull(result.Response);
        Assert.False(string.IsNullOrWhiteSpace(result.Response!.Token));
        Assert.Equal("user@example.com", result.Response.User.Email);

        var user = ctx.Db.Users.Single();
        Assert.Equal("user@example.com", user.Email);
        Assert.NotEqual(Password, user.PasswordHash);

        var verification = new PasswordHasher<Domain.User>()
            .VerifyHashedPassword(user, user.PasswordHash, Password);

        Assert.NotEqual(PasswordVerificationResult.Failed, verification);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmailDifferentCase_ReturnsConflict()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);

        var first = await service.RegisterAsync(
            new RegisterRequest("user@example.com", Password));

        var second = await service.RegisterAsync(
            new RegisterRequest("USER@example.com", Password));

        Assert.Equal(RegisterStatus.Created, first.Status);
        Assert.Equal(RegisterStatus.EmailConflict, second.Status);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("User <user@example.com>")]
    public async Task RegisterAsync_WithInvalidEmail_ReturnsInvalidEmail(string email)
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);

        var result = await service.RegisterAsync(new RegisterRequest(email, Password));

        Assert.Equal(RegisterStatus.InvalidEmail, result.Status);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RegisterAsync_WithInvalidPassword_ReturnsInvalidPassword(string? password)
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);

        var result = await service.RegisterAsync(
            new RegisterRequest("user@example.com", password));

        Assert.Equal(RegisterStatus.InvalidPassword, result.Status);
    }

    [Fact]
    public async Task RegisterAsync_WithTooLongPassword_ReturnsInvalidPassword()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);

        var result = await service.RegisterAsync(
            new RegisterRequest("user@example.com", new string('a', 129)));

        Assert.Equal(RegisterStatus.InvalidPassword, result.Status);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);
        await service.RegisterAsync(new RegisterRequest("user@example.com", Password));

        var result = await service.LoginAsync(
            new LoginRequest("USER@example.com", Password));

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.False(string.IsNullOrWhiteSpace(result.Response!.Token));
        Assert.True(result.Response.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsInvalidCredentials()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);
        await service.RegisterAsync(new RegisterRequest("user@example.com", Password));

        var result = await service.LoginAsync(
            new LoginRequest("user@example.com", "WrongPassword123!"));

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsInvalidCredentials()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);

        var result = await service.LoginAsync(
            new LoginRequest("missing@example.com", Password));

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
    }

    [Fact]
    public async Task GetUserAsync_ReturnsOnlyExistingUsers()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);
        var registered = await service.RegisterAsync(
            new RegisterRequest("user@example.com", Password));

        var user = await service.GetUserAsync(registered.Response!.User.Id);

        Assert.NotNull(user);
        Assert.Equal("user@example.com", user!.Email);
        Assert.Null(await service.GetUserAsync(999));
    }

    [Fact]
    public async Task RegisterAsync_ReturnsTokenWithSubjectAndEmailClaims()
    {
        using var ctx = new SqliteTestContext();
        var service = CreateService(ctx);

        var result = await service.RegisterAsync(
            new RegisterRequest("user@example.com", Password));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Response!.Token);

        Assert.Equal(
            result.Response.User.Id.ToString(CultureInfo.InvariantCulture),
            token.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Sub).Value);

        Assert.Equal(
            "user@example.com",
            token.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Email).Value);

        Assert.Equal("ShortUrl.Tests", token.Issuer);
        Assert.Equal("ShortUrl.Tests", token.Audiences.Single());
        Assert.True(token.ValidTo > DateTime.UtcNow);
    }

    private static AuthService CreateService(SqliteTestContext ctx) =>
        new(
            ctx.Db,
            new PasswordHasher<Domain.User>(),
            new JwtOptions
            {
                Issuer = "ShortUrl.Tests",
                Audience = "ShortUrl.Tests",
                Key = TestKey,
                ExpiryMinutes = 60
            });
}
