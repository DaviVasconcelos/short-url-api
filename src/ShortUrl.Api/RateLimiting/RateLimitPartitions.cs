using System.Threading.RateLimiting;
using ShortUrl.Api.Extensions;

namespace ShortUrl.Api.RateLimiting;

/// <summary>
/// Builds the fixed-window partitions used by the rate limiter. Authenticated requests are
/// keyed by user id so a caller cannot reset the counter by changing IP; anonymous requests
/// fall back to the remote IP.
/// </summary>
public static class RateLimitPartitions
{
    public static RateLimitPartition<string> ForUserOrIp(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.GetUserId() is { } userId ? $"user:{userId}" : IpKey(context),
            _ => CreateLimiter(permitLimit));

    public static RateLimitPartition<string> ForIp(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(IpKey(context), _ => CreateLimiter(permitLimit));

    private static string IpKey(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } ip
            ? $"ip:{ip}"
            : "ip:unknown";

    private static FixedWindowRateLimiterOptions CreateLimiter(int permitLimit) =>
        new()
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        };
}
