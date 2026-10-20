namespace ShortUrl.Domain;

public class ShortUrl
{
    public int Id { get; set; }

    public string ShortCode { get; set; } = null!;

    public string OriginalUrl { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public long ClickCount { get; set; }

    public int? OwnerId { get; set; }
}
