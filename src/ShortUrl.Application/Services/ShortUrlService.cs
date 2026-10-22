using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShortUrl.Application.Dtos;
using ShortUrl.Application.Options;
using ShortUrl.Domain.Abstractions;
using ShortUrl.Infrastructure.Persistence;
using ShortUrlEntity = ShortUrl.Domain.ShortUrl;

namespace ShortUrl.Application.Services;

public sealed class ShortUrlService : IShortUrlService
{
    private const int MaxUrlLength = 2048;
    private const int MaxCodeGenerationAttempts = 3;

    private static readonly Regex AliasPattern =
        new("^[a-zA-Z0-9_-]{3,30}$", RegexOptions.Compiled);

    private static readonly HashSet<string> ReservedAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "api", "urls", "health", "swagger", "favicon.ico"
        };

    private readonly AppDbContext _db;
    private readonly ICodeGenerator _codeGenerator;
    private readonly IMemoryCache _cache;
    private readonly ShortUrlOptions _options;

    public ShortUrlService(
        AppDbContext db,
        ICodeGenerator codeGenerator,
        IMemoryCache cache,
        ShortUrlOptions options)
    {
        _db = db;
        _codeGenerator = codeGenerator;
        _cache = cache;
        _options = options;
    }

    public async Task<CreateShortUrlResult> CreateAsync(
        CreateShortUrlRequest request,
        int? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeUrl(request.Url, out var originalUrl))
            return CreateShortUrlResult.InvalidUrl();

        var alias = string.IsNullOrWhiteSpace(request.CustomAlias) ? null : request.CustomAlias.Trim();
        if (alias is not null && (!AliasPattern.IsMatch(alias) || ReservedAliases.Contains(alias)))
            return CreateShortUrlResult.InvalidAlias();

        var expiresAt = ResolveExpiry(request.ExpiresInDays);

        return alias is not null
            ? await CreateWithAliasAsync(alias, originalUrl, expiresAt, ownerId, cancellationToken)
            : await CreateWithGeneratedCodeAsync(originalUrl, expiresAt, ownerId, cancellationToken);
    }

    public async Task<GetShortUrlResult> GetOriginalAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyFor(shortCode);

        if (_cache.TryGetValue(cacheKey, out CachedShortUrl? cached) && cached is not null)
        {
            if (cached.ExpiresAt is { } cachedExpiry && cachedExpiry <= DateTime.UtcNow)
            {
                _cache.Remove(cacheKey);
                return GetShortUrlResult.Expired();
            }

            var affected = await IncrementClickAsync(shortCode, cancellationToken);
            if (affected == 0)
            {
                _cache.Remove(cacheKey);
                return GetShortUrlResult.NotFound();
            }

            return GetShortUrlResult.Found(cached.OriginalUrl);
        }

        var entity = await _db.ShortUrls
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShortCode == shortCode, cancellationToken);

        if (entity is null)
            return GetShortUrlResult.NotFound();

        if (entity.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return GetShortUrlResult.Expired();

        _cache.Set(
            cacheKey,
            new CachedShortUrl(entity.OriginalUrl, entity.ExpiresAt),
            TimeSpan.FromSeconds(_options.CacheTtlSeconds));

        await IncrementClickAsync(shortCode, cancellationToken);

        return GetShortUrlResult.Found(entity.OriginalUrl);
    }

    public async Task<ShortUrlStatsResponse?> GetStatsAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ShortUrls
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShortCode == shortCode, cancellationToken);

        return entity is null
            ? null
            : new ShortUrlStatsResponse(
                entity.ShortCode,
                entity.OriginalUrl,
                entity.CreatedAt,
                entity.ExpiresAt,
                entity.ClickCount);
    }

    public async Task<DeleteShortUrlResult> DeleteAsync(
        string shortCode,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ShortUrls
            .FirstOrDefaultAsync(x => x.ShortCode == shortCode, cancellationToken);

        if (entity is null)
            return DeleteShortUrlResult.NotFound();

        if (userId is null || entity.OwnerId != userId)
            return DeleteShortUrlResult.Forbidden();

        _db.ShortUrls.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _cache.Remove(CacheKeyFor(shortCode));

        return DeleteShortUrlResult.Deleted();
    }

    private Task<int> IncrementClickAsync(string shortCode, CancellationToken cancellationToken) =>
        _db.ShortUrls
            .Where(x => x.ShortCode == shortCode)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ClickCount, x => x.ClickCount + 1),
                cancellationToken);

    private static string CacheKeyFor(string shortCode) => $"url:{shortCode}";

    private async Task<CreateShortUrlResult> CreateWithAliasAsync(
        string alias,
        string originalUrl,
        DateTime? expiresAt,
        int? ownerId,
        CancellationToken cancellationToken)
    {
        if (await _db.ShortUrls.AnyAsync(x => x.ShortCode == alias, cancellationToken))
            return CreateShortUrlResult.AliasConflict();

        var entity = BuildEntity(alias, originalUrl, expiresAt, ownerId);
        _db.ShortUrls.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(entity).State = EntityState.Detached;
            return CreateShortUrlResult.AliasConflict();
        }

        return CreateShortUrlResult.Created(ToResponse(entity));
    }

    private async Task<CreateShortUrlResult> CreateWithGeneratedCodeAsync(
        string originalUrl,
        DateTime? expiresAt,
        int? ownerId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCodeGenerationAttempts; attempt++)
        {
            var entity = BuildEntity(
                _codeGenerator.Generate(_options.CodeLength),
                originalUrl,
                expiresAt,
                ownerId);
            _db.ShortUrls.Add(entity);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return CreateShortUrlResult.Created(ToResponse(entity));
            }
            catch (DbUpdateException)
            {
                _db.Entry(entity).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique short code after multiple attempts.");
    }

    private static ShortUrlEntity BuildEntity(
        string shortCode,
        string originalUrl,
        DateTime? expiresAt,
        int? ownerId) =>
        new()
        {
            ShortCode = shortCode,
            OriginalUrl = originalUrl,
            ExpiresAt = expiresAt,
            OwnerId = ownerId
        };

    private ShortUrlResponse ToResponse(ShortUrlEntity entity) =>
        new(entity.ShortCode, $"{_options.BaseUrl.TrimEnd('/')}/{entity.ShortCode}", entity.ExpiresAt);

    private DateTime? ResolveExpiry(int? expiresInDays)
    {
        var days = expiresInDays ?? _options.DefaultExpiryDays;
        return days > 0 ? DateTime.UtcNow.AddDays(days) : null;
    }

    private static bool TryNormalizeUrl(string? url, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength)
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (IsBlockedHost(uri.Host))
            return false;

        normalized = uri.ToString();
        return true;
    }

    private static bool IsBlockedHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host, out var ip))
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || bytes[0] == 127
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        return false;
    }
}
