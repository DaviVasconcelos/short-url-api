namespace ShortUrl.Application.Dtos;

public sealed record ShortUrlStatsResponse(
    string ShortCode,
    string OriginalUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    long ClickCount);
