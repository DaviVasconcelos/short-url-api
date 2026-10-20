using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShortUrl.Infrastructure.Persistence;

namespace ShortUrl.Tests.Integration;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "test-only-jwt-signing-key-at-least-32-bytes-long";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ApiFactory()
    {
        _connection.Open();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = "ShortUrl.Tests",
                ["Jwt:Audience"] = "ShortUrl.Tests",
                ["Jwt:ExpiryMinutes"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _connection.Dispose();
    }
}
