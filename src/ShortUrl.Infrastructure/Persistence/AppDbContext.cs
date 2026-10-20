using Domain = ShortUrl.Domain;
using Microsoft.EntityFrameworkCore;

namespace ShortUrl.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected AppDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Domain.ShortUrl> ShortUrls => Set<Domain.ShortUrl>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var shortUrl = modelBuilder.Entity<Domain.ShortUrl>();

        shortUrl.ToTable("ShortUrls");
        shortUrl.HasKey(x => x.Id);

        shortUrl.Property(x => x.ShortCode)
            .IsRequired()
            .HasMaxLength(30);

        shortUrl.Property(x => x.OriginalUrl)
            .IsRequired()
            .HasMaxLength(2048);

        shortUrl.Property(x => x.CreatedAt)
            .IsRequired();

        shortUrl.Property(x => x.ClickCount)
            .IsRequired()
            .HasDefaultValue(0L);

        shortUrl.HasIndex(x => x.ShortCode)
            .IsUnique();

        base.OnModelCreating(modelBuilder);
    }
}
