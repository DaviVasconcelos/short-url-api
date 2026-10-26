namespace ShortUrl.Application.Dtos;

public sealed record PagedShortUrlResponse(
    IReadOnlyList<ShortUrlListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
