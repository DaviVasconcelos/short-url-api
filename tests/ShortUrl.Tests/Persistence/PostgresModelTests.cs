using Microsoft.EntityFrameworkCore;
using ShortUrl.Infrastructure.Postgres.Persistence;

namespace ShortUrl.Tests.Persistence;

public sealed class PostgresModelTests
{
    [Fact]
    public void Model_maps_short_urls_with_postgres_provider()
    {
        var options = new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=shorturl_test;Username=postgres;Password=postgres")
            .Options;

        using var db = new PostgresAppDbContext(options);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("CREATE TABLE \"ShortUrls\"", script);
        Assert.Contains("\"ShortCode\" character varying(30) NOT NULL", script);
        Assert.Contains("\"OriginalUrl\" character varying(2048) NOT NULL", script);
        Assert.Contains("\"ClickCount\" bigint NOT NULL", script);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_ShortUrls_ShortCode\"", script);
    }
}
