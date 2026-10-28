# AGENTS.md

Short URL API — .NET 8 ASP.NET Core URL shortener, built as a portfolio project. Everything here is verified against the current tree. The project is feature-complete: `README.md` matches the code, and `PLAN.md` is the historical build plan.

## Current state

- **Fases 0-7 done — feature-complete.** Solution (`ShortUrl.slnx`) + 5 projects; EF Core + SQLite (`InitialCreate` + `AddUsersAndOwnership`) plus PostgreSQL (`InitialPostgres` + `AddUsersAndOwnership` in `ShortUrl.Infrastructure.Postgres`); `User` entity + nullable `ShortUrl.OwnerId` (FK `SetNull`); Base62 `ICodeGenerator`; `ShortUrlService` (create / redirect / stats / owner-only delete / paginated owner list); controllers (`UrlsController` = optional-auth `POST`+protected `GET` list+public `GET` stats+protected `DELETE` on `api/urls`, `RedirectController` = `GET /{code}` → 302, `AuthController` = `POST register`/`POST login`/`GET me` on `api/auth`); **JWT auth** (`AuthService`, HS256, 60 min, `PasswordHasher<User>`, normalized emails, ephemeral dev key, Swagger Bearer, `401` on `/me`); **ownership** (optional Bearer on create, owner-only list/delete, anonymous links not deletable, `401`/`403`/`404` semantics); rate limiting; health checks; Swagger with XML docs; **`IMemoryCache` cache-aside on the redirect path** (`ShortUrl.CacheTtlSeconds`, default 300; caches only `originalUrl` + `expiresAt`, click increment via `ExecuteUpdateAsync`, invalidated on delete); **container readiness** (`$PORT`, `/health/live` + `/health`, `ForwardedHeaders`, `Swagger:Enabled`, request-derived base URL); **89 xUnit tests** (generators, URL/auth/ownership/listing services, integration, Postgres model, migration retry, and SQLite ownership — all green); **multi-stage `Dockerfile`** (build `sdk:8.0` → runtime `aspnet:8.0`, listens on 8080 or `$PORT`), **`render.yaml`** (Render Blueprint for the public demo) and **`.github/workflows/ci.yml`** (restore/build/test + a `docker build` job). Migrations auto-apply on startup (`DatabaseMigrationRunner`, 5 attempts / 2s, both configurable). README polished: 3-command quickstart, Swagger screenshot (`docs/images/swagger.png`), Mermaid dependency graph. `README.md` (English) and `README.pt-BR.md` (Portuguese) are kept in sync and cross-link at the top.
- **Pending:** roadmap v2 **corte enxuto** active — see `PLAN.md` §12. Fases 1a, 3a, 3b, 3c, 3d, 4a e **4b** concluídas na `main` (89 testes verdes; PostgreSQL em produção no Neon, ownership, JWT, delete protegido, listagem paginada, container readiness e demo pública em **`https://shorturl-davi.onrender.com`**). Próxima entrega: **7a** (consolidação de docs/ADRs). Redis/cleanup são stretch; SQL Server foi cortado.

## Build, test, run

- Solution is `ShortUrl.slnx` (new XML format, not `.sln`). Pass it explicitly: `dotnet build ShortUrl.slnx`, `dotnet test ShortUrl.slnx`.
- `global.json` pins SDK 10.0.401, but every project targets `net8.0`. Do not change `<TargetFramework>`.
- Pin Microsoft packages to `8.0.x` (currently `8.0.31`). `dotnet add package X` without `--version` resolves 10.x and fails with `NU1202`. `Npgsql.EntityFrameworkCore.PostgreSQL` is pinned to `8.0.11`; `System.IdentityModel.Tokens.Jwt` is pinned to `7.7.3` (matches `JwtBearer 8.0.31`'s `Microsoft.IdentityModel.Protocols.OpenIdConnect` dependency).
- Run the API: `dotnet run --project src/ShortUrl.Api` → `http://localhost:5000` (Swagger at `/swagger`, liveness at `/health/live`, readiness at `/health`).
- `launchSettings.json` uses port `5000`; `ShortUrl:BaseUrl` is optional and, when unset, links derive from the request host. Keep the launch profile on `5000` if you want the documented localhost URLs.
- `tests/ShortUrl.Tests` has **89 xUnit tests** across `Generators/`, `Services/`, `Integration/`, and `Persistence/`. Service tests use `Support/SqliteTestContext` (SQLite in-memory — **not** EF InMemory, since the service uses `ExecuteUpdateAsync`; its default `BaseUrl` is `https://short.test`); `Integration/ApiFactory` (`WebApplicationFactory<Program>`) injects a deterministic test `Jwt:Key` and covers URL, auth, ownership, listing, health, Swagger flag, forwarded headers, and base URL flows, with a dedicated auth rate-limit test; `Integration/DatabaseMigrationTests` covers the configurable retry policy (success, retry-then-succeed, exhausted, validation); `Integration/UrlsOwnershipApiTests` covers `401`/`403`/`404`; `Integration/UrlsListApiTests` covers authenticated pagination and isolation; `Persistence/PostgresModelTests` builds the Postgres model without connecting to a database; `Persistence/SqliteOwnershipTests` covers owned vs. anonymous links. `Program.cs` ends with `public partial class Program;` to expose the entry point.
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
- DI wiring happens only in `src/ShortUrl.Api/Program.cs`. Controllers (`src/ShortUrl.Api/Controllers`) consume `IShortUrlService` or `IAuthService`, never `AppDbContext`.
- `ShortUrlService` (in `Application`) is the primary URL service that touches `AppDbContext` and `ICodeGenerator` and owns create / redirect / stats / owner-only delete / paginated owner list; `AuthService` also uses `AppDbContext` for users. Its DI seam is the constructor `(AppDbContext, ICodeGenerator, IMemoryCache, ShortUrlOptions)`. `AddDbContext<AppDbContext, PostgresAppDbContext>` keeps the service provider-agnostic.
- `AuthService` (in `Application`) owns registration/login and token generation. Its DI seam is `(AppDbContext, IPasswordHasher<Domain.User>, JwtOptions)`. `JwtOptions` is a singleton POCO bound from configuration; `Program.cs` assigns `Key` (configured or ephemeral dev key) before registering it.
- `User` lives in `ShortUrl.Domain`; `ShortUrl.OwnerId` is a nullable FK configured with `OnDelete(SetNull)`. Ownership is enforced on create/list/delete: authenticated creates set `OwnerId`; list and delete require the owner; anonymous links are not listable or deletable via the API.

## Gotchas

- Root namespace is `ShortUrl` and the domain entity is also `ShortUrl`, so unqualified `ShortUrl` is ambiguous. Follow the existing aliases: `using Domain = ShortUrl.Domain;` and `using ShortUrlEntity = ShortUrl.Domain.ShortUrl;`.
- `infrastructure` vs docs are now aligned: Dockerfile + CI exist. If the README CI badge is red, check the workflow runs, not the docs.
- Docker: build stage uses `sdk:8.0` and `.dockerignore` **excludes `global.json`** (SDK 8 can't satisfy its `10.0.401` pin; publishing a single project doesn't read `.slnx`, so SDK 8 is enough). CI still needs SDK 10 to parse `ShortUrl.slnx`. The runtime entrypoint listens on `$PORT` when set, otherwise on `8080`; Render sets `PORT` automatically.
- **Do NOT add `Microsoft.AspNetCore.OpenApi` back to `ShortUrl.Api`.** It forces `Microsoft.OpenApi` to `1.6.30`, whose `openapi: "3.0.4"` output makes the swagger-ui bundled in `Swashbuckle.AspNetCore 6.6.2` fail with *"does not specify a valid version field"*. Removing it resolves `Microsoft.OpenApi 1.6.14` (compatible); `AddEndpointsApiExplorer` comes from the shared framework, so the package was never needed.
- `ShortUrlOptions` is a singleton POCO bound via `Configuration.GetSection("ShortUrl")`, not `IOptions<T>`. `JwtOptions` follows the same pattern.
- Database provider is selected by `Database:Provider` (`Sqlite` default, `Postgres` opt-in). Unknown values and Postgres without `ConnectionStrings:Postgres` fail fast in `Program.cs`. In Docker/Render, set `Database__Provider=Postgres` and `ConnectionStrings__Postgres`.
- Npgsql maps `DateTime` to `timestamp with time zone`; keep `CreatedAt`/`ExpiresAt` in UTC (the services already use `DateTime.UtcNow`). SQLite migrations and Postgres migrations have independent histories — never point one startup at the other provider's history.
- `Users.Email` has a unique index, but SQLite/Postgres string comparisons are case-sensitive by default. `AuthService` normalizes emails (trim + lowercase) before insert/login. `ShortUrl.OwnerId` is intentionally nullable so pre-auth links remain anonymous; keep it nullable in future migrations.
- JWT: `Jwt:Key` is deliberately absent from `appsettings.json`. In Development, `Program.cs` generates an ephemeral 32-byte key and logs a warning; outside Development it fails fast unless `Jwt__Key` is set (>=32 bytes). The Docker image runs in `Production`, so `docker run` needs `-e Jwt__Key=...`.
- JwtBearer sets `MapInboundClaims = false`; the user id is read from the `sub` claim. `/api/auth/me` returns `401` for missing/invalid tokens. Delete returns `401` without a valid token, `403` for non-owners/anonymous links, and `404` when missing.
- 3b, 3c, and 3d added no migrations: `Users` and `OwnerId` already exist from 3a. Ownership and listing reuse the same schema and the existing `OwnerId` index.
- Create accepts an optional Bearer token. If an `Authorization` header is present but invalid/expired, `RequireValidOptionalTokenAttribute` returns `401` before body validation; a missing header creates an anonymous link. Delete uses `[Authorize]` plus `ClaimsPrincipalExtensions.GetUserId()`.
- The Docker image defaults to SQLite; it only uses Postgres when the provider env vars are supplied.
- Rate limiting: `"create-url"` (fixed window, 30/min per IP) on `UrlsController.Create` and `"auth"` (10/min per IP) on `AuthController.Register`/`Login`. `OnRejected` returns `429` + `Retry-After`; the message is generic. Partitions use `RemoteIpAddress`, and 4a installs `ForwardedHeaders` first so a proxy-supplied `X-Forwarded-For` is honored (`ForwardLimit=1`, rightmost entry). The list endpoint is authenticated but has no dedicated rate-limit policy (3e stretch).
- Health: `/health/live` maps no checks (liveness only); `/health` runs the tagged `"ready"` DbContext check. Render's `healthCheckPath` should be `/health/live` so a slow database does not restart the service.
- Swagger: controlled by `Swagger:Enabled`, defaulting to `Environment.IsDevelopment()`. Production/Docker needs `Swagger__Enabled=true` to expose it. The flag is read from `app.Configuration` after `Build()` so `WebApplicationFactory` overrides work.
- Deploy: `render.yaml` is the source of truth for the Render service (`shorturl-davi`, free plan, region `virginia`, `healthCheckPath: /health/live`, `autoDeployTrigger: checksPass`). Live at `https://shorturl-davi.onrender.com`. `ConnectionStrings__Postgres` is `sync: false` (dashboard secret), `Jwt__Key` is `generateValue: true`. **Use the Neon direct host (no `-pooler`) in the connection string** — the pooled endpoint is PgBouncer in transaction mode and Neon recommends direct connections for migrations. Full runbook: `docs/deploy.md`.
- The public smoke test is not automated (it needs the live host and would pollute the demo database). Validate a deploy manually with the checklist in `docs/deploy.md`: health, Swagger, anonymous create returning an `https://` link, redirect, auth, owner list/delete, `401`/`403`/`404`, and a redirect after the next redeploy to prove Neon persistence. `410` cannot be triggered through the API (`expiresInDays <= 0` means "never expires") and is covered by service tests instead.
- `DatabaseMigrationRunner` (`src/ShortUrl.Api/Configuration/DatabaseMigration.cs`) wraps `db.Database.Migrate()` with retries; `Database:MigrateAttempts` (default `5`) and `Database:MigrateDelaySeconds` (default `2`, max `60`) are read at startup and validated. Each failed attempt is logged as a warning and the final failure as an error before the exception is rethrown, so Render logs show the real cause. Keep the retry helper pure (`Action` + `ILogger`) so it stays unit-testable without a database.
- Base URL: `ShortUrlOptions.BaseUrl` is nullable and absent from `appsettings.json`. `UrlsController.ResolveBaseUrl()` uses the configured value when present; otherwise `Request.GetBaseUrl()` (`scheme://host` + path base, already reflecting `X-Forwarded-Proto`). `ShortUrlService` accepts an optional `baseUrl` on create/list and falls back to the configured value, then to a relative `/{code}`.
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
