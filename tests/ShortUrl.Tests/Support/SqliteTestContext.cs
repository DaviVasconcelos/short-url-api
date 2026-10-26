using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShortUrl.Application.Options;
using ShortUrl.Application.Services;
using ShortUrl.Infrastructure.Persistence;
using ShortUrl.Infrastructure.Security;

namespace ShortUrl.Tests.Support;

public sealed class SqliteTestContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestContext(Action<ShortUrlOptions>? configure = null)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(dbOptions);
        Db.Database.EnsureCreated();

        Options = new ShortUrlOptions { BaseUrl = "https://short.test" };
        configure?.Invoke(Options);

        Cache = new MemoryCache(new MemoryCacheOptions());
        Service = new ShortUrlService(Db, new Base62CodeGenerator(), Cache, Options);
    }

    public AppDbContext Db { get; }

    public MemoryCache Cache { get; }

    public ShortUrlOptions Options { get; }

    public ShortUrlService Service { get; }

    public void Dispose()
    {
        Db.Dispose();
        Cache.Dispose();
        _connection.Dispose();
    }
}
