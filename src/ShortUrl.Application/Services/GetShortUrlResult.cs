namespace ShortUrl.Application.Services;

public enum GetShortUrlStatus
{
    Found,
    NotFound,
    Expired
}

public sealed record GetShortUrlResult(GetShortUrlStatus Status, string? OriginalUrl)
{
    public static GetShortUrlResult Found(string originalUrl) =>
        new(GetShortUrlStatus.Found, originalUrl);

    public static GetShortUrlResult NotFound() =>
        new(GetShortUrlStatus.NotFound, null);

    public static GetShortUrlResult Expired() =>
        new(GetShortUrlStatus.Expired, null);
}
