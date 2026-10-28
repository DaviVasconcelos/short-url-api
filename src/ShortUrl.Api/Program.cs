using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ShortUrl.Api.Configuration;
using ShortUrl.Api.Errors;
using ShortUrl.Application.Options;
using ShortUrl.Application.Services;
using ShortUrl.Domain.Abstractions;
using ShortUrl.Infrastructure.Persistence;
using ShortUrl.Infrastructure.Postgres.Persistence;
using ShortUrl.Infrastructure.Security;
using Domain = ShortUrl.Domain;

var builder = WebApplication.CreateBuilder(args);

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";

switch (databaseProvider.ToLowerInvariant())
{
    case "sqlite":
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
        break;

    case "postgres":
        var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres must be configured when Database:Provider is Postgres.");
        }

        builder.Services.AddDbContext<AppDbContext, PostgresAppDbContext>(options =>
            options.UseNpgsql(postgresConnectionString));
        break;

    default:
        throw new InvalidOperationException(
            $"Unsupported database provider '{databaseProvider}'. Use 'Sqlite' or 'Postgres'.");
}

var shortUrlOptions = builder.Configuration.GetSection("ShortUrl").Get<ShortUrlOptions>()
    ?? new ShortUrlOptions();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? new JwtOptions();

var jwtKey = builder.Configuration["Jwt:Key"];
var generatedDevelopmentKey = false;

if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Jwt:Key must be configured in non-development environments.");
    }

    jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    generatedDevelopmentKey = true;
}

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 bytes (256 bits) long.");
}

if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
    string.IsNullOrWhiteSpace(jwtOptions.Audience))
{
    throw new InvalidOperationException(
        "Jwt:Issuer and Jwt:Audience must be configured.");
}

if (jwtOptions.ExpiryMinutes <= 0 || jwtOptions.ExpiryMinutes > 1440)
{
    throw new InvalidOperationException(
        "Jwt:ExpiryMinutes must be between 1 and 1440.");
}

jwtOptions.Key = jwtKey;

builder.Services.AddSingleton(shortUrlOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICodeGenerator, Base62CodeGenerator>();
builder.Services.AddScoped<IPasswordHasher<Domain.User>, PasswordHasher<Domain.User>>();
builder.Services.AddScoped<IShortUrlService, ShortUrlService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT returned by /api/auth/register or /api/auth/login."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = Array.Empty<string>()
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

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

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
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
                Detail = "The rate limit has been reached. Try again later."
            },
            cancellationToken);
    };
});

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: new[] { "ready" });

var app = builder.Build();

var swaggerEnabled = app.Configuration.GetValue<bool?>("Swagger:Enabled")
    ?? app.Environment.IsDevelopment();

if (generatedDevelopmentKey)
{
    app.Logger.LogWarning(
        "Jwt:Key was not configured; using an ephemeral development key. Tokens will be invalidated on restart.");
}

var migrationOptions = DatabaseMigrationOptions.FromConfiguration(app.Configuration);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DatabaseMigrationRunner.Run(() => db.Database.Migrate(), migrationOptions, app.Logger);
}

app.UseForwardedHeaders(ForwardedHeadersSetup.CreateOptions());
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/", () => swaggerEnabled
        ? Results.Redirect("/swagger")
        : Results.Ok(new
        {
            name = "Short URL API",
            swagger = "/swagger",
            health = "/health/live",
            endpoints = new[]
            {
                "/api/urls",
                "/api/auth/register",
                "/api/auth/login",
                "/api/auth/me"
            }
        }))
    .ExcludeFromDescription();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
