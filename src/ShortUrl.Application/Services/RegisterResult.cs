using ShortUrl.Application.Dtos;

namespace ShortUrl.Application.Services;

public enum RegisterStatus
{
    Created,
    EmailConflict,
    InvalidEmail,
    InvalidPassword
}

public sealed record RegisterResult(RegisterStatus Status, AuthResponse? Response)
{
    public static RegisterResult Created(AuthResponse response) =>
        new(RegisterStatus.Created, response);

    public static RegisterResult EmailConflict() =>
        new(RegisterStatus.EmailConflict, null);

    public static RegisterResult InvalidEmail() =>
        new(RegisterStatus.InvalidEmail, null);

    public static RegisterResult InvalidPassword() =>
        new(RegisterStatus.InvalidPassword, null);
}
