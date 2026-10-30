namespace ShortUrl.Application.Options;

public sealed class RateLimitingOptions
{
    /// <summary>Requests per minute for URL creation, per user (or per IP when anonymous).</summary>
    public int CreatePerMinute { get; set; } = 30;

    /// <summary>Requests per minute for the owner listing, per user.</summary>
    public int ListPerMinute { get; set; } = 60;

    /// <summary>Requests per minute for deletes, per user.</summary>
    public int DeletePerMinute { get; set; } = 30;

    /// <summary>Requests per minute for register/login, per IP.</summary>
    public int AuthPerMinute { get; set; } = 10;
}
