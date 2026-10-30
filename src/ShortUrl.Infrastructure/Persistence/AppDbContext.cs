using Microsoft.EntityFrameworkCore;
using Domain = ShortUrl.Domain;

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

    public DbSet<Domain.User> Users => Set<Domain.User>();

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

        shortUrl.HasOne<Domain.User>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        shortUrl.HasIndex(x => x.OwnerId);

        // Supports the expired-link cleanup: filter on ExpiresAt, ordered by it.
        shortUrl.HasIndex(x => x.ExpiresAt);

        var user = modelBuilder.Entity<Domain.User>();

        user.ToTable("Users");
        user.HasKey(x => x.Id);

        user.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(320);

        user.Property(x => x.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);

        user.Property(x => x.CreatedAt)
            .IsRequired();

        user.HasIndex(x => x.Email)
            .IsUnique();

        base.OnModelCreating(modelBuilder);
    }
}
