namespace ShortUrl.Application.Dtos;

public sealed record CreateShortUrlRequest(string? Url, string? CustomAlias, int? ExpiresInDays);
