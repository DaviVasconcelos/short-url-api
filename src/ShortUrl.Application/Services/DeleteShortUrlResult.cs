namespace ShortUrl.Application.Services;

public enum DeleteShortUrlStatus
{
    Deleted,
    NotFound,
    Forbidden
}

public sealed record DeleteShortUrlResult(DeleteShortUrlStatus Status)
{
    public static DeleteShortUrlResult Deleted() =>
        new(DeleteShortUrlStatus.Deleted);

    public static DeleteShortUrlResult NotFound() =>
        new(DeleteShortUrlStatus.NotFound);

    public static DeleteShortUrlResult Forbidden() =>
        new(DeleteShortUrlStatus.Forbidden);
}
