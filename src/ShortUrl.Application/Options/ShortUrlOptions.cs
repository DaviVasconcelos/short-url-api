namespace ShortUrl.Application.Options;

public sealed class ShortUrlOptions
{
    public string? BaseUrl { get; set; }

    public int CodeLength { get; set; } = 7;

    public int DefaultExpiryDays { get; set; } = 30;

    public int CacheTtlSeconds { get; set; } = 300;
}
