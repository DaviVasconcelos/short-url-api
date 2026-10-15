using ShortUrl.Application.Dtos;

namespace ShortUrl.Application.Services;

public interface IShortUrlService
{
    Task<CreateShortUrlResult> CreateAsync(
        CreateShortUrlRequest request,
        CancellationToken cancellationToken = default);

    Task<GetShortUrlResult> GetOriginalAsync(
        string shortCode,
        CancellationToken cancellationToken = default);

    Task<ShortUrlStatsResponse?> GetStatsAsync(
        string shortCode,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string shortCode,
        CancellationToken cancellationToken = default);
}
