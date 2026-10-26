using ShortUrl.Application.Dtos;
using ShortUrl.Application.Services;
using ShortUrl.Tests.Support;
using Domain = ShortUrl.Domain;
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
    public async Task CreateAsync_WithOwnerAndGeneratedCode_AssignsOwnerId()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");

        var result = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/owned", null, 30),
            owner.Id);

        Assert.Equal(CreateShortUrlStatus.Created, result.Status);

        var entity = ctx.Db.ShortUrls.Single(x => x.ShortCode == result.Response!.ShortCode);
        Assert.Equal(owner.Id, entity.OwnerId);
    }

    [Fact]
    public async Task CreateAsync_WithOwnerAndCustomAlias_AssignsOwnerId()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");

        var result = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/owned", "owned-link", 30),
            owner.Id);

        Assert.Equal(CreateShortUrlStatus.Created, result.Status);

        var entity = ctx.Db.ShortUrls.Single(x => x.ShortCode == "owned-link");
        Assert.Equal(owner.Id, entity.OwnerId);
    }

    [Fact]
    public async Task CreateAsync_WithoutOwner_LeavesOwnerIdNull()
    {
        using var ctx = new SqliteTestContext();

        var result = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/anonymous", null, 30));

        Assert.Equal(CreateShortUrlStatus.Created, result.Status);

        var entity = ctx.Db.ShortUrls.Single(x => x.ShortCode == result.Response!.ShortCode);
        Assert.Null(entity.OwnerId);
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
    public async Task DeleteAsync_ByOwner_DeletesAndEvictsCacheEntry()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");
        var created = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/temp", null, 30),
            owner.Id);
        var code = created.Response!.ShortCode;
        await ctx.Service.GetOriginalAsync(code);

        var deleted = await ctx.Service.DeleteAsync(code, owner.Id);
        var lookup = await ctx.Service.GetOriginalAsync(code);

        Assert.Equal(DeleteShortUrlStatus.Deleted, deleted.Status);
        Assert.False(ctx.Cache.TryGetValue($"url:{code}", out object? _));
        Assert.Equal(GetShortUrlStatus.NotFound, lookup.Status);
    }

    [Fact]
    public async Task DeleteAsync_ByNonOwner_ReturnsForbiddenAndKeepsLink()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");
        var other = await AddUserAsync(ctx, "other@example.com");
        var created = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/protected", null, 30),
            owner.Id);
        var code = created.Response!.ShortCode;

        var result = await ctx.Service.DeleteAsync(code, other.Id);

        Assert.Equal(DeleteShortUrlStatus.Forbidden, result.Status);
        Assert.NotNull(ctx.Db.ShortUrls.SingleOrDefault(x => x.ShortCode == code));
    }

    [Fact]
    public async Task DeleteAsync_ForAnonymousLink_ReturnsForbidden()
    {
        using var ctx = new SqliteTestContext();
        var user = await AddUserAsync(ctx, "user@example.com");
        var created = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/anonymous", null, 30));
        var code = created.Response!.ShortCode;

        var result = await ctx.Service.DeleteAsync(code, user.Id);

        Assert.Equal(DeleteShortUrlStatus.Forbidden, result.Status);
        Assert.NotNull(ctx.Db.ShortUrls.SingleOrDefault(x => x.ShortCode == code));
    }

    [Fact]
    public async Task DeleteAsync_WithoutUser_ReturnsForbidden()
    {
        using var ctx = new SqliteTestContext();
        var created = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/anonymous", null, 30));
        var code = created.Response!.ShortCode;

        var result = await ctx.Service.DeleteAsync(code);

        Assert.Equal(DeleteShortUrlStatus.Forbidden, result.Status);
        Assert.NotNull(ctx.Db.ShortUrls.SingleOrDefault(x => x.ShortCode == code));
    }

    [Fact]
    public async Task DeleteAsync_WhenCodeDoesNotExist_ReturnsNotFound()
    {
        using var ctx = new SqliteTestContext();

        var result = await ctx.Service.DeleteAsync("doesnotexist", 1);

        Assert.Equal(DeleteShortUrlStatus.NotFound, result.Status);
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

    [Fact]
    public async Task GetUserLinksAsync_ReturnsOnlyOwnedLinksIncludingExpired()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");
        var other = await AddUserAsync(ctx, "other@example.com");
        var now = DateTime.UtcNow;

        ctx.Db.ShortUrls.AddRange(
            new ShortUrlEntity
            {
                ShortCode = "owned-active",
                OriginalUrl = "https://example.com/active",
                OwnerId = owner.Id,
                CreatedAt = now.AddMinutes(-1)
            },
            new ShortUrlEntity
            {
                ShortCode = "owned-expired",
                OriginalUrl = "https://example.com/expired",
                OwnerId = owner.Id,
                CreatedAt = now.AddMinutes(-2),
                ExpiresAt = now.AddMinutes(-1)
            },
            new ShortUrlEntity
            {
                ShortCode = "other-link",
                OriginalUrl = "https://example.com/other",
                OwnerId = other.Id,
                CreatedAt = now
            },
            new ShortUrlEntity
            {
                ShortCode = "anon-link",
                OriginalUrl = "https://example.com/anonymous",
                CreatedAt = now
            });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.GetUserLinksAsync(owner.Id, 1, 20);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            new[] { "owned-active", "owned-expired" },
            result.Items.Select(x => x.ShortCode).ToArray());
    }

    [Fact]
    public async Task GetUserLinksAsync_OrdersByCreatedAtDescendingThenByIdDescending()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");
        var sameTime = DateTime.UtcNow.AddMinutes(-5);

        ctx.Db.ShortUrls.AddRange(
            new ShortUrlEntity
            {
                ShortCode = "tie-first",
                OriginalUrl = "https://example.com/tie-first",
                OwnerId = owner.Id,
                CreatedAt = sameTime
            },
            new ShortUrlEntity
            {
                ShortCode = "newest",
                OriginalUrl = "https://example.com/newest",
                OwnerId = owner.Id,
                CreatedAt = sameTime.AddMinutes(1)
            },
            new ShortUrlEntity
            {
                ShortCode = "tie-second",
                OriginalUrl = "https://example.com/tie-second",
                OwnerId = owner.Id,
                CreatedAt = sameTime
            },
            new ShortUrlEntity
            {
                ShortCode = "oldest",
                OriginalUrl = "https://example.com/oldest",
                OwnerId = owner.Id,
                CreatedAt = sameTime.AddMinutes(-1)
            });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.GetUserLinksAsync(owner.Id, 1, 20);

        Assert.Equal(
            new[] { "newest", "tie-second", "tie-first", "oldest" },
            result.Items.Select(x => x.ShortCode).ToArray());
    }

    [Fact]
    public async Task GetUserLinksAsync_ReturnsRequestedPageWithMetadata()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");
        var now = DateTime.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            ctx.Db.ShortUrls.Add(new ShortUrlEntity
            {
                ShortCode = $"link{i}",
                OriginalUrl = $"https://example.com/{i}",
                OwnerId = owner.Id,
                CreatedAt = now.AddMinutes(-i)
            });
        }

        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.GetUserLinksAsync(owner.Id, 2, 2);

        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(
            new[] { "link2", "link3" },
            result.Items.Select(x => x.ShortCode).ToArray());
    }

    [Fact]
    public async Task GetUserLinksAsync_WhenPageIsBeyondLast_ReturnsEmptyItems()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");
        ctx.Db.ShortUrls.Add(new ShortUrlEntity
        {
            ShortCode = "only-link",
            OriginalUrl = "https://example.com/only",
            OwnerId = owner.Id
        });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.GetUserLinksAsync(owner.Id, 5, 2);

        Assert.Empty(result.Items);
        Assert.Equal(5, result.Page);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task GetUserLinksAsync_WhenUserHasNoLinks_ReturnsEmptyPage()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");

        var result = await ctx.Service.GetUserLinksAsync(owner.Id, 1, 20);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task GetUserLinksAsync_MapsListItemFields()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");
        var createdAt = DateTime.UtcNow.AddMinutes(-3);
        var expiresAt = DateTime.UtcNow.AddDays(7);

        ctx.Db.ShortUrls.Add(new ShortUrlEntity
        {
            ShortCode = "mapped-link",
            OriginalUrl = "https://example.com/mapped",
            OwnerId = owner.Id,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            ClickCount = 7
        });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.GetUserLinksAsync(owner.Id, 1, 20);
        var item = Assert.Single(result.Items);

        Assert.Equal("mapped-link", item.ShortCode);
        Assert.Equal($"{ctx.Options.BaseUrl!.TrimEnd('/')}/mapped-link", item.ShortUrl);
        Assert.Equal("https://example.com/mapped", item.OriginalUrl);
        Assert.Equal(createdAt, item.CreatedAt);
        Assert.Equal(expiresAt, item.ExpiresAt);
        Assert.Equal(7L, item.ClickCount);
    }

    [Fact]
    public async Task CreateAsync_WithRequestBaseUrl_PrefersItOverConfiguredBaseUrl()
    {
        using var ctx = new SqliteTestContext();

        var result = await ctx.Service.CreateAsync(
            new CreateShortUrlRequest("https://example.com/public", null, 30),
            baseUrl: "https://public.example");

        Assert.Equal(CreateShortUrlStatus.Created, result.Status);
        Assert.Equal($"https://public.example/{result.Response!.ShortCode}", result.Response.ShortUrl);
    }

    [Fact]
    public async Task GetUserLinksAsync_WithRequestBaseUrl_PrefersItOverConfiguredBaseUrl()
    {
        using var ctx = new SqliteTestContext();
        var owner = await AddUserAsync(ctx, "owner@example.com");

        ctx.Db.ShortUrls.Add(new ShortUrlEntity
        {
            ShortCode = "public-link",
            OriginalUrl = "https://example.com/public",
            OwnerId = owner.Id
        });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.GetUserLinksAsync(
            owner.Id,
            1,
            20,
            baseUrl: "https://public.example");

        var item = Assert.Single(result.Items);
        Assert.Equal("https://public.example/public-link", item.ShortUrl);
    }

    private static async Task<Domain.User> AddUserAsync(
        SqliteTestContext ctx,
        string email)
    {
        var user = new Domain.User
        {
            Email = email,
            PasswordHash = "hash"
        };

        ctx.Db.Users.Add(user);
        await ctx.Db.SaveChangesAsync();

        return user;
    }
}
