using ShortUrl.Application.Dtos;
using ShortUrl.Application.Services;
using ShortUrl.Tests.Support;
using ShortUrlEntity = ShortUrl.Domain.ShortUrl;

namespace ShortUrl.Tests.Services;

public sealed class ShortUrlServiceTests
{
    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://localhost/internal")]
    [InlineData("http://127.0.0.1/internal")]
    public async Task CreateAsync_WithInvalidOrBlockedUrl_ReturnsInvalidUrl(string url)
    {
        using var ctx = new SqliteTestContext();

        var result = await ctx.Service.CreateAsync(new CreateShortUrlRequest(url, null, 30));

        Assert.Equal(CreateShortUrlStatus.InvalidUrl, result.Status);
    }

    [Theory]
    [InlineData("swagger")]
    [InlineData("API")]
    [InlineData("ab")]
    public async Task CreateAsync_WithReservedOrMalformedAlias_ReturnsInvalidAlias(string alias)
    {
        using var ctx = new SqliteTestContext();

        var result = await ctx.Service.CreateAsync(new CreateShortUrlRequest("https://example.com", alias, 30));

        Assert.Equal(CreateShortUrlStatus.InvalidAlias, result.Status);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateAlias_ReturnsAliasConflict()
    {
        using var ctx = new SqliteTestContext();

        var first = await ctx.Service.CreateAsync(new CreateShortUrlRequest("https://example.com/a", "my-alias", 30));
        var second = await ctx.Service.CreateAsync(new CreateShortUrlRequest("https://example.com/b", "my-alias", 30));

        Assert.Equal(CreateShortUrlStatus.Created, first.Status);
        Assert.Equal(CreateShortUrlStatus.AliasConflict, second.Status);
    }

    [Fact]
    public async Task CreateAsync_WithGeneratedCode_ReturnsBase62CodeAndShortUrl()
    {
        using var ctx = new SqliteTestContext();

        var result = await ctx.Service.CreateAsync(new CreateShortUrlRequest("https://example.com/portfolio", null, 30));

        Assert.Equal(CreateShortUrlStatus.Created, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(ctx.Options.CodeLength, result.Response!.ShortCode.Length);
        Assert.EndsWith($"/{result.Response.ShortCode}", result.Response.ShortUrl);
    }

    [Fact]
    public async Task GetOriginalAsync_IncrementsClickCount()
    {
        using var ctx = new SqliteTestContext();
        var created = await ctx.Service.CreateAsync(new CreateShortUrlRequest("https://example.com/hot", null, 30));
        var code = created.Response!.ShortCode;

        var first = await ctx.Service.GetOriginalAsync(code);
        var second = await ctx.Service.GetOriginalAsync(code);
        var stats = await ctx.Service.GetStatsAsync(code);

        Assert.Equal(GetShortUrlStatus.Found, first.Status);
        Assert.Equal(GetShortUrlStatus.Found, second.Status);
        Assert.Equal("https://example.com/hot", first.OriginalUrl);
        Assert.NotNull(stats);
        Assert.Equal(2L, stats!.ClickCount);
    }

    [Fact]
    public async Task GetOriginalAsync_PopulatesCacheWithUrlAndExpiry()
    {
        using var ctx = new SqliteTestContext();
        var created = await ctx.Service.CreateAsync(new CreateShortUrlRequest("https://example.com/cached", null, 30));
        var code = created.Response!.ShortCode;

        await ctx.Service.GetOriginalAsync(code);

        Assert.True(ctx.Cache.TryGetValue($"url:{code}", out var cachedValue));
        var cached = Assert.IsType<CachedShortUrl>(cachedValue);
        Assert.Equal("https://example.com/cached", cached.OriginalUrl);
        Assert.Equal(created.Response.ExpiresAt, cached.ExpiresAt);
    }

    [Fact]
    public async Task DeleteAsync_EvictsCacheEntry_AndSubsequentLookupIsNotFound()
    {
        using var ctx = new SqliteTestContext();
        var created = await ctx.Service.CreateAsync(new CreateShortUrlRequest("https://example.com/temp", null, 30));
        var code = created.Response!.ShortCode;
        await ctx.Service.GetOriginalAsync(code);

        var deleted = await ctx.Service.DeleteAsync(code);
        var lookup = await ctx.Service.GetOriginalAsync(code);

        Assert.True(deleted);
        Assert.False(ctx.Cache.TryGetValue($"url:{code}", out object? _));
        Assert.Equal(GetShortUrlStatus.NotFound, lookup.Status);
    }

    [Fact]
    public async Task GetOriginalAsync_WhenExpired_ReturnsExpired()
    {
        using var ctx = new SqliteTestContext();
        ctx.Db.ShortUrls.Add(new ShortUrlEntity
        {
            ShortCode = "expired1",
            OriginalUrl = "https://example.com/gone",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.GetOriginalAsync("expired1");

        Assert.Equal(GetShortUrlStatus.Expired, result.Status);
    }

    [Fact]
    public async Task GetOriginalAsync_WithUnknownCode_ReturnsNotFound()
    {
        using var ctx = new SqliteTestContext();

        var result = await ctx.Service.GetOriginalAsync("doesnotexist");

        Assert.Equal(GetShortUrlStatus.NotFound, result.Status);
    }
}
