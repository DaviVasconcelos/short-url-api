namespace ShortUrl.Application.Options;

public sealed class CleanupOptions
{
    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; } = 60;

    public int RetentionDays { get; set; } = 30;

    public int BatchSize { get; set; } = 500;
}
