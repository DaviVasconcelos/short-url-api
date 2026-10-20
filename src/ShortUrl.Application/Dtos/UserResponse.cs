namespace ShortUrl.Application.Dtos;

public sealed record UserResponse(int Id, string Email, DateTime CreatedAt);
