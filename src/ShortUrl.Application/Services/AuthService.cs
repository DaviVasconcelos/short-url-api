using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShortUrl.Application.Dtos;
using ShortUrl.Application.Options;
using ShortUrl.Infrastructure.Persistence;
using Domain = ShortUrl.Domain;

namespace ShortUrl.Application.Services;

public sealed class AuthService : IAuthService
{
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;
    private const int MaxEmailLength = 320;

    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Domain.User> _passwordHasher;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        AppDbContext db,
        IPasswordHasher<Domain.User> passwordHasher,
        JwtOptions jwtOptions)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtOptions = jwtOptions;
    }

    public async Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeEmail(request.Email, out var email))
            return RegisterResult.InvalidEmail();

        var password = request.Password;

        if (!IsValidPassword(password))
            return RegisterResult.InvalidPassword();

        if (await _db.Users.AnyAsync(x => x.Email == email, cancellationToken))
            return RegisterResult.EmailConflict();

        var user = new Domain.User
        {
            Email = email,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password!);
        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(user).State = EntityState.Detached;
            return RegisterResult.EmailConflict();
        }

        return RegisterResult.Created(CreateAuthResponse(user));
    }

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeEmail(request.Email, out var email))
            return LoginResult.InvalidCredentials();

        var password = request.Password;

        if (!IsValidPassword(password))
            return LoginResult.InvalidCredentials();

        var user = await _db.Users
            .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is null)
            return LoginResult.InvalidCredentials();

        var verification = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            password!);

        if (verification == PasswordVerificationResult.Failed)
            return LoginResult.InvalidCredentials();

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password!);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return LoginResult.Success(CreateAuthResponse(user));
    }

    public async Task<UserResponse?> GetUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new UserResponse(x.Id, x.Email, x.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private AuthResponse CreateAuthResponse(Domain.User user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_jwtOptions.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials);

        var encodedToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new AuthResponse(
            encodedToken,
            expiresAt,
            new UserResponse(user.Id, user.Email, user.CreatedAt));
    }

    private static bool TryNormalizeEmail(string? email, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(email))
            return false;

        var trimmed = email.Trim();

        if (trimmed.Length > MaxEmailLength)
            return false;

        if (!MailAddress.TryCreate(trimmed, out var parsed))
            return false;

        if (!string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            return false;

        normalized = trimmed.ToLowerInvariant();
        return true;
    }

    private static bool IsValidPassword(string? password) =>
        password is not null &&
        password.Length >= MinPasswordLength &&
        password.Length <= MaxPasswordLength;
}
