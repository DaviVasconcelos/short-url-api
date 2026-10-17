namespace ShortUrl.Application.Services;

public sealed record CachedShortUrl(string OriginalUrl, DateTime? ExpiresAt);
