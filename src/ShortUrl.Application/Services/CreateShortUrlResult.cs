using ShortUrl.Application.Dtos;

namespace ShortUrl.Application.Services;

public enum CreateShortUrlStatus
{
    Created,
    InvalidUrl,
    InvalidAlias,
    AliasConflict,
    InvalidExpiry
}

public sealed record CreateShortUrlResult(CreateShortUrlStatus Status, ShortUrlResponse? Response)
{
    public static CreateShortUrlResult Created(ShortUrlResponse response) =>
        new(CreateShortUrlStatus.Created, response);

    public static CreateShortUrlResult InvalidUrl() =>
        new(CreateShortUrlStatus.InvalidUrl, null);

    public static CreateShortUrlResult InvalidAlias() =>
        new(CreateShortUrlStatus.InvalidAlias, null);

    public static CreateShortUrlResult AliasConflict() =>
        new(CreateShortUrlStatus.AliasConflict, null);

    public static CreateShortUrlResult InvalidExpiry() =>
        new(CreateShortUrlStatus.InvalidExpiry, null);
}
