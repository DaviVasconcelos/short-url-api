namespace ShortUrl.Application.Dtos;

public sealed record ShortUrlListItemResponse(
    string ShortCode,
    string ShortUrl,
    string OriginalUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    long ClickCount);
