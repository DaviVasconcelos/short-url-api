using ShortUrl.Application.Dtos;

namespace ShortUrl.Application.Services;

public enum LoginStatus
{
    Success,
    InvalidCredentials
}

public sealed record LoginResult(LoginStatus Status, AuthResponse? Response)
{
    public static LoginResult Success(AuthResponse response) =>
        new(LoginStatus.Success, response);

    public static LoginResult InvalidCredentials() =>
        new(LoginStatus.InvalidCredentials, null);
}
