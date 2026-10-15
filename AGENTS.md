# AGENTS.md

Short URL API — .NET 8 ASP.NET Core URL shortener, built as a portfolio project. Everything here is verified against the current tree. Note that `README.md` and `PLAN.md` describe more than exists yet.

## Build, test, run

- Solution is `ShortUrl.slnx` (new XML format, not `.sln`). Pass it explicitly: `dotnet build ShortUrl.slnx`, `dotnet test ShortUrl.slnx`.
- `global.json` pins SDK 10.0.401, but every project targets `net8.0`. Do not change `<TargetFramework>`.
- Pin Microsoft packages to `8.0.x` (currently `8.0.31`). `dotnet add package X` without `--version` resolves 10.x and fails with `NU1202`.
- Run the API: `dotnet run --project src/ShortUrl.Api` (Swagger at `/swagger`).
- No tests exist yet — `dotnet test` finds none (planned in `PLAN.md` Fase 5).

## EF Core migrations

- `AppDbContext` is in `src/ShortUrl.Infrastructure/Persistence`; migrations sit in `Persistence/Migrations`.
- Add: `dotnet ef migrations add <Name> --project src/ShortUrl.Infrastructure --startup-project src/ShortUrl.Api --output-dir Persistence/Migrations`
- Apply: same command with `database update` instead of `migrations add` (creates `shorturl.db`, gitignored).
- `Microsoft.EntityFrameworkCore.Design` lives in the Api (startup) project.

## Architecture — dependency direction is inverted from the norm

- Projects under `src/`: `ShortUrl.Domain`, `ShortUrl.Application`, `ShortUrl.Infrastructure`, `ShortUrl.Api`.
- Edges: `Api -> Application -> Infrastructure`, plus `Application -> Domain` and `Infrastructure -> Domain`.
- **`Application` references `Infrastructure`, not the reverse.** `Infrastructure` must never reference `Application`. Any interface that `Infrastructure` implements must live in `ShortUrl.Domain` (e.g. `ICodeGenerator` is in `Domain/Abstractions`, not `Application`).
- DI wiring happens only in `src/ShortUrl.Api/Program.cs`. The (not-yet-written) controllers must consume `IShortUrlService`, never `AppDbContext`.

## Gotchas

- Root namespace is `ShortUrl` and the domain entity is also `ShortUrl`, so unqualified `ShortUrl` is ambiguous. Follow the existing aliases: `using Domain = ShortUrl.Domain;` and `using ShortUrlEntity = ShortUrl.Domain.ShortUrl;`.
- Reality vs. docs: no controllers/endpoints yet (only DI + Swagger), no `Dockerfile`, no `.github/workflows`. The README CI badge points at a workflow that does not exist.
- Port mismatch: `launchSettings.json` runs the `http` profile on `localhost:5212`, while `appsettings.json` has `ShortUrl:BaseUrl` = `http://localhost:5000`. Generated `shortUrl` values use the config value.
- `ShortUrlOptions` is a singleton POCO bound via `Configuration.GetSection("ShortUrl")`, not `IOptions<T>`.

## Conventions

- Commits use Conventional Commits (`feat:`, `docs:`, `chore:`). Default branch: `main`.
- `PLAN.md` is written in Portuguese; `README.md` in English.

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues (repo `DaviVasconcelos/short-url-api`), managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
