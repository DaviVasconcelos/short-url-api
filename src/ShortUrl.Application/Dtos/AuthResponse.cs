namespace ShortUrl.Application.Dtos;

public sealed record AuthResponse(string Token, DateTime ExpiresAt, UserResponse User);
