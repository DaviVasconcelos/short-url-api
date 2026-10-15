namespace ShortUrl.Application.Options;

public sealed class ShortUrlOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5000";

    public int CodeLength { get; set; } = 7;

    public int DefaultExpiryDays { get; set; } = 30;
}
