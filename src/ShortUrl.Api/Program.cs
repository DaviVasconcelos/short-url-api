using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Api.Errors;
using ShortUrl.Application.Options;
using ShortUrl.Application.Services;
using ShortUrl.Domain.Abstractions;
using ShortUrl.Infrastructure.Persistence;
using ShortUrl.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

var shortUrlOptions = builder.Configuration.GetSection("ShortUrl").Get<ShortUrlOptions>()
    ?? new ShortUrlOptions();

builder.Services.AddSingleton(shortUrlOptions);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICodeGenerator, Base62CodeGenerator>();
builder.Services.AddScoped<IShortUrlService, ShortUrlService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("create-url", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = "The rate limit for creating short URLs has been reached. Try again later."
            },
            cancellationToken);
    };
});

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    MigrateDatabase(db);
}

app.UseExceptionHandler();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

static void MigrateDatabase(AppDbContext db)
{
    const int maxAttempts = 5;

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            return;
        }
        catch (Exception) when (attempt < maxAttempts)
        {
            Thread.Sleep(TimeSpan.FromSeconds(2));
        }
    }
}

public partial class Program;
