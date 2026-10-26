namespace ShortUrl.Api.Extensions;

public static class HttpRequestExtensions
{
    public static string GetBaseUrl(this HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}";
}
