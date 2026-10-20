using ShortUrl.Application.Dtos;

namespace ShortUrl.Application.Services;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<UserResponse?> GetUserAsync(
        int userId,
        CancellationToken cancellationToken = default);
}
