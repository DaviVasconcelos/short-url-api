using ShortUrl.Application.Options;
using ShortUrl.Application.Services;

namespace ShortUrl.Api.BackgroundServices;

/// <summary>
/// Runs <see cref="IExpiredLinkCleaner"/> once at startup and then every
/// <see cref="CleanupOptions.IntervalMinutes"/>. Only registered when
/// <c>Cleanup:Enabled</c> is true, so development and tests never delete data by surprise.
/// </summary>
public sealed class ExpiredLinkCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CleanupOptions _options;
    private readonly ILogger<ExpiredLinkCleanupService> _logger;

    public ExpiredLinkCleanupService(
        IServiceScopeFactory scopeFactory,
        CleanupOptions options,
        ILogger<ExpiredLinkCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        try
        {
            do
            {
                await RunCleanupAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The service is a singleton and AppDbContext is scoped, so every run needs its own scope.
            using var scope = _scopeFactory.CreateScope();
            var cleaner = scope.ServiceProvider.GetRequiredService<IExpiredLinkCleaner>();

            await cleaner.DeleteExpiredAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Expired link cleanup failed; the next attempt runs in {IntervalMinutes} minute(s).",
                _options.IntervalMinutes);
        }
    }
}
