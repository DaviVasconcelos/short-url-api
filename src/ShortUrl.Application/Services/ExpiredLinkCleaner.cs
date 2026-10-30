using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShortUrl.Application.Options;
using ShortUrl.Infrastructure.Persistence;

namespace ShortUrl.Application.Services;

public sealed class ExpiredLinkCleaner : IExpiredLinkCleaner
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly CleanupOptions _options;
    private readonly ILogger<ExpiredLinkCleaner> _logger;

    public ExpiredLinkCleaner(
        AppDbContext db,
        TimeProvider clock,
        CleanupOptions options,
        ILogger<ExpiredLinkCleaner> logger)
    {
        _db = db;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-_options.RetentionDays);
        var deleted = 0;

        while (true)
        {
            // Select the ids first: ExecuteDeleteAsync does not translate Take reliably on
            // both providers, and ordering by ExpiresAt lets the expiry index do the work.
            var ids = await _db.ShortUrls
                .Where(x => x.ExpiresAt != null && x.ExpiresAt < cutoff)
                .OrderBy(x => x.ExpiresAt)
                .Select(x => x.Id)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
                break;

            deleted += await _db.ShortUrls
                .Where(x => ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);

            if (ids.Count < _options.BatchSize)
                break;
        }

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Expired link cleanup removed {DeletedCount} row(s) expired before {Cutoff:O}.",
                deleted,
                cutoff);
        }

        return deleted;
    }
}
