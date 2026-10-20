# AGENTS.md

Short URL API — .NET 8 ASP.NET Core URL shortener, built as a portfolio project. Everything here is verified against the current tree. The project is feature-complete: `README.md` matches the code, and `PLAN.md` is the historical build plan.

## Current state

- **Fases 0-7 done — feature-complete.** Solution (`ShortUrl.slnx`) + 5 projects; EF Core + SQLite (`InitialCreate` + `AddUsersAndOwnership`) plus PostgreSQL (`InitialPostgres` + `AddUsersAndOwnership` in `ShortUrl.Infrastructure.Postgres`); `User` entity + nullable `ShortUrl.OwnerId` (FK `SetNull`); `PasswordHasher<User>` registered for the future auth flow; Base62 `ICodeGenerator`; `ShortUrlService` (create / redirect / stats / delete); controllers (`UrlsController` = `POST`+`GET` stats+`DELETE` on `api/urls`, `RedirectController` = `GET /{code}` → 302); rate limiting; health checks; Swagger with XML docs; **`IMemoryCache` cache-aside on the redirect path** (`ShortUrl.CacheTtlSeconds`, default 300; caches only `originalUrl` + `expiresAt`, click increment via `ExecuteUpdateAsync`, invalidated on delete); **24 xUnit tests** (generator, service, integration, Postgres model, and SQLite ownership — all green); **multi-stage `Dockerfile`** (build `sdk:8.0` → runtime `aspnet:8.0`, listens on 8080) and **`.github/workflows/ci.yml`** (restore/build/test + a `docker build` job). Migrations auto-apply on startup (`Program.cs` `MigrateDatabase`, 5 attempts). README polished: 3-command quickstart, Swagger screenshot (`docs/images/swagger.png`), Mermaid dependency graph. `README.md` (English) and `README.pt-BR.md` (Portuguese) are kept in sync and cross-link at the top.
- **Pending:** roadmap v2 **corte enxuto** active — see `PLAN.md` §12. Fases 1a e 3a concluídas na `main` (24 testes verdes; provider PostgreSQL e schema de ownership). Próxima entrega: **3b** (register/login/me + JWT + 401/403). Redis/cleanup são stretch; SQL Server foi cortado.

## Build, test, run

- Solution is `ShortUrl.slnx` (new XML format, not `.sln`). Pass it explicitly: `dotnet build ShortUrl.slnx`, `dotnet test ShortUrl.slnx`.
- `global.json` pins SDK 10.0.401, but every project targets `net8.0`. Do not change `<TargetFramework>`.
- Pin Microsoft packages to `8.0.x` (currently `8.0.31`). `dotnet add package X` without `--version` resolves 10.x and fails with `NU1202`. `Npgsql.EntityFrameworkCore.PostgreSQL` is pinned to `8.0.11`.
- Run the API: `dotnet run --project src/ShortUrl.Api` → `http://localhost:5000` (Swagger at `/swagger`, health at `/health`).
- `launchSettings.json` and `ShortUrl:BaseUrl` both use port `5000`; keep them in sync.
- `tests/ShortUrl.Tests` has **24 xUnit tests** across `Generators/`, `Services/`, `Integration/`, and `Persistence/`. Service tests use `Support/SqliteTestContext` (SQLite in-memory — **not** EF InMemory, since the service uses `ExecuteUpdateAsync`); the integration test uses `Integration/ApiFactory` (`WebApplicationFactory<Program>`); `Persistence/PostgresModelTests` builds the Postgres model without connecting to a database; `Persistence/SqliteOwnershipTests` covers owned vs. anonymous links. `Program.cs` ends with `public partial class Program;` to expose the entry point.
- `src/ShortUrl.Api/ShortUrl.Api.http` has ready-to-run requests for the whole flow.

## EF Core migrations

- `AppDbContext` is in `src/ShortUrl.Infrastructure/Persistence`; SQLite migrations sit in `Persistence/Migrations` (`InitialCreate`, `AddUsersAndOwnership`).
- `PostgresAppDbContext` is in `src/ShortUrl.Infrastructure.Postgres/Persistence`; PostgreSQL migrations sit in that project (`InitialPostgres`, `AddUsersAndOwnership`). It derives from `AppDbContext` and only changes the provider.
- Add SQLite: `dotnet ef migrations add <Name> --project src/ShortUrl.Infrastructure --startup-project src/ShortUrl.Api --context AppDbContext --output-dir Persistence/Migrations`
- Add PostgreSQL: `dotnet ef migrations add <Name> --project src/ShortUrl.Infrastructure.Postgres --startup-project src/ShortUrl.Api --context PostgresAppDbContext --output-dir Persistence/Migrations`
- **Every model change needs two migrations** (one per provider). Generate both in the same commit.
- Apply: same commands with `database update` instead of `migrations add` (SQLite creates `shorturl.db`, gitignored). For Postgres, set `Database__Provider=Postgres` and `ConnectionStrings__Postgres` first. **Migrations also auto-apply on app startup** (`MigrateDatabase` in `Program.cs`), so Docker/deploy needs no manual step.
- `Microsoft.EntityFrameworkCore.Design` lives in the Api (startup) project. `src/ShortUrl.Api/Persistence/PostgresDesignTimeFactory.cs` lets `dotnet ef` target Postgres without a running database (it reads `ConnectionStrings__Postgres` or uses a localhost fallback).

## Architecture — dependency direction is inverted from the norm

- Projects under `src/`: `ShortUrl.Domain`, `ShortUrl.Application`, `ShortUrl.Infrastructure`, `ShortUrl.Infrastructure.Postgres`, `ShortUrl.Api`.
- Edges: `Api -> Application -> Infrastructure`, plus `Api -> Infrastructure.Postgres -> Infrastructure`, `Application -> Domain` and `Infrastructure -> Domain`.
- **`Application` references `Infrastructure`, not the reverse.** `Infrastructure` must never reference `Application`. Any interface that `Infrastructure` implements must live in `ShortUrl.Domain` (e.g. `ICodeGenerator` is in `Domain/Abstractions`, not `Application`).
- DI wiring happens only in `src/ShortUrl.Api/Program.cs`. Controllers (`src/ShortUrl.Api/Controllers`) consume `IShortUrlService`, never `AppDbContext`.
- `ShortUrlService` (in `Application`) is the one place that touches `AppDbContext` and `ICodeGenerator`. Its DI seam is the constructor `(AppDbContext, ICodeGenerator, IMemoryCache, ShortUrlOptions)`. `AddDbContext<AppDbContext, PostgresAppDbContext>` keeps the service provider-agnostic.
- `User` lives in `ShortUrl.Domain`; `ShortUrl.OwnerId` is a nullable FK configured with `OnDelete(SetNull)`. `Program.cs` registers `IPasswordHasher<Domain.User>` (`PasswordHasher<User>`) for the auth flow; no auth service exists yet.

## Gotchas

- Root namespace is `ShortUrl` and the domain entity is also `ShortUrl`, so unqualified `ShortUrl` is ambiguous. Follow the existing aliases: `using Domain = ShortUrl.Domain;` and `using ShortUrlEntity = ShortUrl.Domain.ShortUrl;`.
- `infrastructure` vs docs are now aligned: Dockerfile + CI exist. If the README CI badge is red, check the workflow runs, not the docs.
- Docker: build stage uses `sdk:8.0` and `.dockerignore` **excludes `global.json`** (SDK 8 can't satisfy its `10.0.401` pin; publishing a single project doesn't read `.slnx`, so SDK 8 is enough). CI still needs SDK 10 to parse `ShortUrl.slnx`.
- **Do NOT add `Microsoft.AspNetCore.OpenApi` back to `ShortUrl.Api`.** It forces `Microsoft.OpenApi` to `1.6.30`, whose `openapi: "3.0.4"` output makes the swagger-ui bundled in `Swashbuckle.AspNetCore 6.6.2` fail with *"does not specify a valid version field"*. Removing it resolves `Microsoft.OpenApi 1.6.14` (compatible); `AddEndpointsApiExplorer` comes from the shared framework, so the package was never needed.
- `ShortUrlOptions` is a singleton POCO bound via `Configuration.GetSection("ShortUrl")`, not `IOptions<T>`.
- Database provider is selected by `Database:Provider` (`Sqlite` default, `Postgres` opt-in). Unknown values and Postgres without `ConnectionStrings:Postgres` fail fast in `Program.cs`. In Docker/Render, set `Database__Provider=Postgres` and `ConnectionStrings__Postgres`.
- Npgsql maps `DateTime` to `timestamp with time zone`; keep `CreatedAt`/`ExpiresAt` in UTC (the service already uses `DateTime.UtcNow`). SQLite migrations and Postgres migrations have independent histories — never point one startup at the other provider's history.
- `Users.Email` has a unique index, but SQLite/Postgres string comparisons are case-sensitive by default. Normalize email (trim + lowercase) before inserting in 3b. `ShortUrl.OwnerId` is intentionally nullable so pre-auth links remain anonymous; keep it nullable in future migrations.
- The Docker image defaults to SQLite; it only uses Postgres when the provider env vars are supplied.
- Rate limiting: policy name `"create-url"` (fixed window, 30/min per IP), applied via `[EnableRateLimiting("create-url")]` on `UrlsController.Create`. `OnRejected` returns `429` + `Retry-After`.
- Reserved aliases rejected on create (case-insensitive): `api`, `urls`, `health`, `swagger`, `favicon.ico`. Keep this list in sync with new root routes.
- `ShortUrl.Api.csproj` sets `GenerateDocumentationFile` + `<NoWarn>CS1591</NoWarn>`; add XML `<summary>`/`<response>` on new actions (consumed by Swagger via `IncludeXmlComments`).
- `RedirectController` is hidden from Swagger (`[ApiExplorerSettings(IgnoreApi = true)]`) and owns the root route `/{code}`; literal routes like `/health` win over it in routing.

## Conventions

- Commits use Conventional Commits (`feat:`, `docs:`, `chore:`). Default branch: `main`.
- `PLAN.md` is written in Portuguese; `README.md` in English and `README.pt-BR.md` in Portuguese (keep both in sync).

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues (repo `DaviVasconcelos/short-url-api`), managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
