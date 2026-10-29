using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ShortUrl.Tests.Integration;

/// <summary>
/// Boots the real API against PostgreSQL instead of the in-memory SQLite used by
/// <see cref="ApiFactory"/>. The connection string comes from the
/// <c>SHORTURL_TEST_POSTGRES</c> environment variable, and startup migrations apply
/// the PostgreSQL history before the first request, exactly like production.
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<Program>
{
    public const string ConnectionStringVariable = "SHORTURL_TEST_POSTGRES";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = ConnectionString
            ?? throw new InvalidOperationException(
                $"{ConnectionStringVariable} must be set to use {nameof(PostgresApiFactory)}.");

        // UseSetting is applied before Program.cs reads Database:Provider, which is what
        // makes this exercise the same branch production uses. ConfigureAppConfiguration
        // alone runs later and would leave the provider on SQLite.
        builder.UseSetting("Database:Provider", "Postgres");
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = ApiFactory.TestJwtKey,
                ["Jwt:Issuer"] = "ShortUrl.Tests",
                ["Jwt:Audience"] = "ShortUrl.Tests",
                ["Jwt:ExpiryMinutes"] = "60"
            });
        });
    }
}
