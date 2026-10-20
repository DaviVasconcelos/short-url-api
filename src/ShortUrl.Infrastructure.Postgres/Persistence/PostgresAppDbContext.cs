using Microsoft.EntityFrameworkCore;
using ShortUrl.Infrastructure.Persistence;

namespace ShortUrl.Infrastructure.Postgres.Persistence;

public sealed class PostgresAppDbContext : AppDbContext
{
    public PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options)
        : base(options)
    {
    }
}
