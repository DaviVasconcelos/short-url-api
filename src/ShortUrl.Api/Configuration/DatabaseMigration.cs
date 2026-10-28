namespace ShortUrl.Api.Configuration;

/// <summary>
/// Retry policy for the EF Core migration step that runs on startup.
/// </summary>
public sealed record DatabaseMigrationOptions(int Attempts, TimeSpan RetryDelay)
{
    public const int DefaultAttempts = 5;
    public const int DefaultRetryDelaySeconds = 2;
    private const int MaxRetryDelaySeconds = 60;

    public static DatabaseMigrationOptions FromConfiguration(IConfiguration configuration)
    {
        var attempts = configuration.GetValue<int?>("Database:MigrateAttempts")
            ?? DefaultAttempts;
        var delaySeconds = configuration.GetValue<int?>("Database:MigrateDelaySeconds")
            ?? DefaultRetryDelaySeconds;

        if (attempts < 1)
        {
            throw new InvalidOperationException(
                "Database:MigrateAttempts must be at least 1.");
        }

        if (delaySeconds < 0 || delaySeconds > MaxRetryDelaySeconds)
        {
            throw new InvalidOperationException(
                $"Database:MigrateDelaySeconds must be between 0 and {MaxRetryDelaySeconds}.");
        }

        return new DatabaseMigrationOptions(attempts, TimeSpan.FromSeconds(delaySeconds));
    }
}

public static class DatabaseMigrationRunner
{
    public static void Run(
        Action migrate,
        DatabaseMigrationOptions options,
        ILogger logger)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                migrate();

                if (attempt > 1)
                {
                    logger.LogInformation(
                        "Database migrations applied on attempt {Attempt} of {Attempts}.",
                        attempt,
                        options.Attempts);
                }

                return;
            }
            catch (Exception exception) when (attempt < options.Attempts)
            {
                logger.LogWarning(
                    exception,
                    "Database migration attempt {Attempt} of {Attempts} failed; retrying in {DelaySeconds}s.",
                    attempt,
                    options.Attempts,
                    options.RetryDelay.TotalSeconds);

                Thread.Sleep(options.RetryDelay);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Database migration failed after {Attempts} attempts. The application cannot start.",
                    options.Attempts);

                throw;
            }
        }
    }
}
