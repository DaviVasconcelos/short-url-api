using Microsoft.EntityFrameworkCore;
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
builder.Services.AddSingleton<ICodeGenerator, Base62CodeGenerator>();
builder.Services.AddScoped<IShortUrlService, ShortUrlService>();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
