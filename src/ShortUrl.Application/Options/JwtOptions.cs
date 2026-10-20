namespace ShortUrl.Application.Options;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "ShortUrl.Api";

    public string Audience { get; set; } = "ShortUrl.Api";

    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
