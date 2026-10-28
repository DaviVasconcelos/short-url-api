using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ShortUrl.Api.Configuration;

namespace ShortUrl.Tests.Integration;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public void FromConfiguration_WithNoOverrides_UsesDefaults()
    {
        var options = DatabaseMigrationOptions.FromConfiguration(BuildConfiguration());

        Assert.Equal(5, options.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryDelay);
    }

    [Fact]
    public void FromConfiguration_ReadsOverrides()
    {
        var options = DatabaseMigrationOptions.FromConfiguration(BuildConfiguration(
            ("Database:MigrateAttempts", "3"),
            ("Database:MigrateDelaySeconds", "0")));

        Assert.Equal(3, options.Attempts);
        Assert.Equal(TimeSpan.Zero, options.RetryDelay);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void FromConfiguration_RejectsNonPositiveAttempts(string attempts)
    {
        var configuration = BuildConfiguration(("Database:MigrateAttempts", attempts));

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseMigrationOptions.FromConfiguration(configuration));

        Assert.Contains("Database:MigrateAttempts", exception.Message);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("61")]
    public void FromConfiguration_RejectsOutOfRangeDelay(string delaySeconds)
    {
        var configuration = BuildConfiguration(
            ("Database:MigrateDelaySeconds", delaySeconds));

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseMigrationOptions.FromConfiguration(configuration));

        Assert.Contains("Database:MigrateDelaySeconds", exception.Message);
    }

    [Fact]
    public void Run_WhenFirstAttemptSucceeds_DoesNotRetry()
    {
        var attempts = 0;

        DatabaseMigrationRunner.Run(
            () => attempts++,
            new DatabaseMigrationOptions(5, TimeSpan.Zero),
            NullLogger.Instance);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Run_WhenAttemptsFailAndThenSucceed_RetriesUntilSuccess()
    {
        var attempts = 0;

        DatabaseMigrationRunner.Run(
            () =>
            {
                attempts++;

                if (attempts < 3)
                    throw new InvalidOperationException("database is waking up");
            },
            new DatabaseMigrationOptions(5, TimeSpan.Zero),
            NullLogger.Instance);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Run_WhenEveryAttemptFails_ThrowsAfterConfiguredAttempts()
    {
        var attempts = 0;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseMigrationRunner.Run(
                () =>
                {
                    attempts++;
                    throw new InvalidOperationException("database is unreachable");
                },
                new DatabaseMigrationOptions(3, TimeSpan.Zero),
                NullLogger.Instance));

        Assert.Equal(3, attempts);
        Assert.Equal("database is unreachable", exception.Message);
    }

    private static IConfiguration BuildConfiguration(
        params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
