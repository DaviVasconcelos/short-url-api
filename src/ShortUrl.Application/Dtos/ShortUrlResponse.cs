namespace ShortUrl.Application.Dtos;

public sealed record ShortUrlResponse(string ShortCode, string ShortUrl, DateTime? ExpiresAt);
