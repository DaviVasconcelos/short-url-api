using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ShortUrl.Infrastructure.Postgres.Persistence;

namespace ShortUrl.Api.Persistence;

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=shorturl;Username=postgres;Password=postgres";

    public PostgresAppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PostgresAppDbContext(options);
    }
}
