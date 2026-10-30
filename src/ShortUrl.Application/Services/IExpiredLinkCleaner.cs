namespace ShortUrl.Application.Services;

public interface IExpiredLinkCleaner
{
    /// <summary>
    /// Deletes links whose expiration is older than the configured retention window,
    /// in batches, and returns how many rows were removed.
    /// </summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
