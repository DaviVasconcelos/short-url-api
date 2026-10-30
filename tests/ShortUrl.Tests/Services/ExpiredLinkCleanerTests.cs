using Microsoft.Extensions.Logging.Abstractions;
using ShortUrl.Application.Options;
using ShortUrl.Application.Services;
using ShortUrl.Tests.Support;
using ShortUrlEntity = ShortUrl.Domain.ShortUrl;

namespace ShortUrl.Tests.Services;

public sealed class ExpiredLinkCleanerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeleteExpiredAsync_RemovesLinksPastTheRetentionWindow()
    {
        using var ctx = new SqliteTestContext();
        AddLink(ctx, "gone", Now.UtcDateTime.AddDays(-45));
        AddLink(ctx, "recent", Now.UtcDateTime.AddDays(-5));

        var deleted = await CreateCleaner(ctx).DeleteExpiredAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(new[] { "recent" }, RemainingCodes(ctx));
    }

    [Fact]
    public async Task DeleteExpiredAsync_KeepsLinksInsideTheRetentionWindow()
    {
        using var ctx = new SqliteTestContext();
        AddLink(ctx, "just-expired", Now.UtcDateTime.AddDays(-5));

        var deleted = await CreateCleaner(ctx).DeleteExpiredAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(new[] { "just-expired" }, RemainingCodes(ctx));
    }

    [Fact]
    public async Task DeleteExpiredAsync_LeavesLinksWithoutExpiry()
    {
        using var ctx = new SqliteTestContext();
        AddLink(ctx, "forever", null);

        var deleted = await CreateCleaner(ctx).DeleteExpiredAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(new[] { "forever" }, RemainingCodes(ctx));
    }

    [Fact]
    public async Task DeleteExpiredAsync_ProcessesEveryBatch()
    {
        using var ctx = new SqliteTestContext();

        for (var i = 0; i < 5; i++)
        {
            AddLink(ctx, $"gone-{i}", Now.UtcDateTime.AddDays(-40 - i));
        }

        var deleted = await CreateCleaner(ctx, new CleanupOptions { RetentionDays = 30, BatchSize = 2 })
            .DeleteExpiredAsync();

        Assert.Equal(5, deleted);
        Assert.Empty(RemainingCodes(ctx));
    }

    [Fact]
    public async Task DeleteExpiredAsync_IsIdempotent()
    {
        using var ctx = new SqliteTestContext();
        AddLink(ctx, "gone", Now.UtcDateTime.AddDays(-45));
        var cleaner = CreateCleaner(ctx);

        var first = await cleaner.DeleteExpiredAsync();
        var second = await cleaner.DeleteExpiredAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    private static ExpiredLinkCleaner CreateCleaner(
        SqliteTestContext ctx,
        CleanupOptions? options = null) =>
        new(
            ctx.Db,
            new FakeTimeProvider(Now),
            options ?? new CleanupOptions { RetentionDays = 30 },
            NullLogger<ExpiredLinkCleaner>.Instance);

    private static void AddLink(SqliteTestContext ctx, string shortCode, DateTime? expiresAt)
    {
        ctx.Db.ShortUrls.Add(new ShortUrlEntity
        {
            ShortCode = shortCode,
            OriginalUrl = $"https://example.com/{shortCode}",
            ExpiresAt = expiresAt
        });
        ctx.Db.SaveChanges();
    }

    private static string[] RemainingCodes(SqliteTestContext ctx) =>
        ctx.Db.ShortUrls.OrderBy(x => x.ShortCode).Select(x => x.ShortCode).ToArray();
}
