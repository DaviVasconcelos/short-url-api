namespace ShortUrl.Tests.Integration;

/// <summary>
/// A <see cref="FactAttribute"/> that only runs when <c>SHORTURL_TEST_POSTGRES</c>
/// points at a reachable PostgreSQL database. The CI <c>postgres</c> job sets it;
/// local runs report these tests as skipped instead of failing.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(PostgresApiFactory.ConnectionStringVariable)))
        {
            Skip = $"{PostgresApiFactory.ConnectionStringVariable} is not set.";
        }
    }
}
