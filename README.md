# Short URL API

[![CI](https://github.com/DaviVasconcelos/short-url-api/actions/workflows/ci.yml/badge.svg)](https://github.com/DaviVasconcelos/short-url-api/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![License MIT](https://img.shields.io/badge/license-MIT-green)

A minimal, well-architected URL shortener built with **C# and ASP.NET Core 8**. Focus on clean REST semantics, EF Core, caching and good engineering practices.

> 🇧🇷 PT-BR: API de encurtador de URLs feito com .NET 8. Veja o plano de desenvolvimento em `PLAN.md`.
>
> **SDK vs runtime:** tooling usa **.NET 10 SDK + solution `ShortUrl.slnx`** (requer VS 2022 17.12+), mas o código roda em **.NET 8 LTS** (`<TargetFramework>net8.0</TargetFramework>` em todos os projetos).

## Features

- `POST /api/urls` — shorten a URL (supports custom alias + expiration)
- `GET /{code}` — 302 redirect to original URL + click tracking
- `GET /api/urls/{code}` — stats (original URL, createdAt, expiresAt, clickCount)
- `DELETE /api/urls/{code}` — delete a short URL
- Validation (http/https only, max 2048 chars), 409 on alias conflict, 410 on expired link
- In-memory cache for hot redirect path, fixed-window rate limiting on creation
- Swagger, HealthChecks (`/health`), Docker, GitHub Actions CI, xUnit tests

## Tech stack

- **.NET 8 / ASP.NET Core** (Controllers) — `net8.0` em todos os `.csproj`
- **.NET 10 SDK + `ShortUrl.slnx`** apenas como tooling (solution formato novo)
- **EF Core + SQLite** (swap to SQL Server via connection string)
- **IMemoryCache**, **Rate Limiting** (built-in .NET 8)
- **Swagger / Swashbuckle**, **xUnit + Moq**, **WebApplicationFactory** for integration test
- **Docker** multi-stage build

## Getting started

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) (for `slnx` tooling) + [.NET 8 runtime/SDK](https://dotnet.microsoft.com/download) (target `net8.0`), VS 2022 17.12+ or VS Code + C# Dev Kit, and optionally Docker.

```bash
git clone https://github.com/DaviVasconcelos/short-url-api
cd short-url-api
dotnet restore ShortUrl.slnx
dotnet ef database update --project src/ShortUrl.Infrastructure --startup-project src/ShortUrl.Api
dotnet run --project src/ShortUrl.Api
```

Open Swagger: http://localhost:5000/swagger

With Docker:

```bash
docker build -t shorturl .
docker run -p 5000:8080 shorturl
```

Run tests:

```bash
dotnet test
```

## API reference

| Method | Route | Success | Description |
|--------|-------|---------|-------------|
| POST | `/api/urls` | `201 Created` | Shorten URL |
| GET | `/{code}` | `302 Found` | Redirect |
| GET | `/api/urls/{code}` | `200 OK` | Stats |
| DELETE | `/api/urls/{code}` | `204 No Content` | Delete |
| GET | `/health` | `200 OK` | Health check |

Example:

```http
POST /api/urls
Content-Type: application/json

{ "url": "https://dotnet.microsoft.com/en-us/learn", "customAlias": null, "expiresInDays": 30 }
```

```json
{
  "shortCode": "aB3x9Q",
  "shortUrl": "http://localhost:5000/aB3x9Q",
  "expiresAt": "2026-11-13T00:00:00Z"
}
```

Error codes: `400` invalid URL, `404` not found, `409` alias in use, `410` expired, `429` rate limited.

## Project structure

```
ShortUrl.slnx               # Solution (new XML format, SDK 10 / VS 2022 17.12+)
global.json                # Pins .NET 10 SDK for tooling; runtime stays net8.0
src/
  ShortUrl.Api/            # Controllers, Program.cs, DI, rate limiter, Swagger
  ShortUrl.Application/    # DTOs, IShortUrlService, ShortUrlService, ICodeGenerator
  ShortUrl.Infrastructure/ # AppDbContext, EF configs, Base62 CodeGenerator
tests/
  ShortUrl.Tests/          # Unit + 1 integration test
```

Dependency rule: `Api -> Application -> Infrastructure`. Controllers never touch `DbContext` directly.

## Technical decisions

- **302 (not 301)** for redirects so expiration and click counting keep working (browsers cache 301 aggressively).
- **Base62 7-char codes** via `RandomNumberGenerator` (~3.5T combos) + UNIQUE index + retry on collision.
- **IMemoryCache** (5 min TTL) for the redirect path; Redis would be the next step in production.
- **SQLite** for zero-setup local dev; EF Core makes switching to SQL Server a connection-string change.
- **Fixed-window rate limiting** (30 req/min/IP on POST) to prevent abuse.

## Configuration

`appsettings.json`:

```json
{
  "ConnectionStrings": { "Default": "Data Source=shorturl.db" },
  "ShortUrl": { "BaseUrl": "http://localhost:5000", "CodeLength": 7, "DefaultExpiryDays": 30 }
}
```

## Roadmap

- [ ] JWT auth + per-user links (next portfolio project)
- [ ] Redis distributed cache
- [ ] BackgroundService to purge expired links
- [ ] SQL Server sample connection string
- [ ] Public demo deploy (Fly.io / Render)

## License

MIT — feel free to use as a template.
