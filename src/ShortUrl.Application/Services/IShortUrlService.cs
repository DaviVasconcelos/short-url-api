using ShortUrl.Application.Dtos;

namespace ShortUrl.Application.Services;

public interface IShortUrlService
{
    Task<CreateShortUrlResult> CreateAsync(
        CreateShortUrlRequest request,
        int? ownerId = null,
        CancellationToken cancellationToken = default);

    Task<GetShortUrlResult> GetOriginalAsync(
        string shortCode,
        CancellationToken cancellationToken = default);

    Task<ShortUrlStatsResponse?> GetStatsAsync(
        string shortCode,
        CancellationToken cancellationToken = default);

    Task<PagedShortUrlResponse> GetUserLinksAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<DeleteShortUrlResult> DeleteAsync(
        string shortCode,
        int? userId = null,
        CancellationToken cancellationToken = default);
}
