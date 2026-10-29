# Glossary

Single source of truth for the project's vocabulary. Terms stay in English because that is how they appear in the code, in tests and in endpoint names; definitions are in Portuguese to match `PLAN.md`.

Use these names when writing issues, tests, PRs and ADRs. If a concept is missing here, either it does not exist in the domain yet or the vocabulary is drifting.

## Terms

| Term | Definition | Where it lives |
|---|---|---|
| `ShortUrl` | Record binding a `shortCode` to an `originalUrl`. The central domain entity. | `src/ShortUrl.Domain/ShortUrl.cs` |
| `shortCode` | Short, unique Base62 identifier. It is the trailing segment of the short URL. | `ICodeGenerator`, `ShortUrl.ShortCode` |
| `originalUrl` | Redirect target. Validated as an absolute `http`/`https` URL on create (max 2048 chars). | `ShortUrl.OriginalUrl` |
| `expiresAt` | UTC instant when the link stops redirecting; `null` means it never expires. `expiresInDays` omitted uses `ShortUrl:DefaultExpiryDays` (30); `<= 0` means "never". | `ShortUrl.ExpiresAt`, `ResolveExpiry` |
| `clickCount` | Number of successful redirects. Never cached; incremented straight in the database. | `ShortUrl.ClickCount`, `ExecuteUpdateAsync` |
| `ownerId` | Owner of the link (`User.Id`). `null` means an anonymous link. FK configured with `OnDelete(SetNull)`. | `ShortUrl.OwnerId` |
| Anonymous link | A `ShortUrl` with `ownerId = null`. Anyone can create and follow it, but it is never listed and cannot be deleted through the API. | `ShortUrlService` |
| Owner | Authenticated user who created the link with a Bearer token. The only one allowed to list or delete it. | `IShortUrlService.DeleteAsync` |
| Redirect | `GET /{shortCode}` → `302` to `originalUrl`, counting the click. | `Controllers/RedirectController.cs` |
| Stats | Public read of one link (`shortCode`, `originalUrl`, `createdAt`, `expiresAt`, `clickCount`). | `GET /api/urls/{shortCode}` |
| Custom alias | Caller-supplied `shortCode` accepted on create. Must match `^[a-zA-Z0-9_-]{3,30}$` and not be reserved. | `AliasPattern` in `ShortUrlService` |
| Reserved alias | A `shortCode` that would shadow an app route: `api`, `urls`, `health`, `swagger`, `favicon.ico`. Rejected on create. | `ReservedAliases` in `ShortUrlService` |
| Cache-aside | Redirect path pattern: read the cache, fall back to the database, then populate the cache. Stores only `originalUrl` + `expiresAt`. | `ShortUrlService.GetOriginalAsync` |
| `cacheTtlSeconds` | Lifetime of a cache entry. Default `300`. | `ShortUrl:CacheTtlSeconds` |
| `baseUrl` | Prefix used in the short links returned by create/list. Configured value, else derived from the request, else a relative `/{code}`. | `ShortUrlOptions.BaseUrl`, `Request.GetBaseUrl()` |
| Liveness | Proof the process is up, without touching dependencies. | `GET /health/live` |
| Readiness | Proof the service can serve traffic: includes the database check tagged `ready`. | `GET /health` |
| Provider | Database in use. `Sqlite` is the default, `Postgres` is opt-in through configuration. | `Database:Provider` |
| Migration | Versioned schema change. Each provider keeps its own history. | `src/ShortUrl.Infrastructure/Persistence/Migrations`, same path in `ShortUrl.Infrastructure.Postgres` |
| Corte enxuto | The active scope of roadmap v2 (section 12 of `PLAN.md`). Stretch and cut items stay out of it. | `PLAN.md` §12 |

## Terms we avoid

| Instead of | Use |
|---|---|
| link, URL shortener record (in technical prose) | `ShortUrl` |
| slug, alias, code | `shortCode` (unless it is a `custom alias`) |
| hit, access | click (`clickCount`) |
| anonymous user | anonymous link (`ownerId = null`) — the anonymous creator is not an entity |
| DB, database | "banco" in prose, `AppDbContext` in code |

## Concepts that do not exist

Considered and left out of the active scope — do not treat them as if they existed:

- refresh tokens, roles/admin, password reset, e-mail verification;
- analytics beyond `clickCount`;
- multi-tenancy, horizontal scaling, distributed cache (Redis is a stretch goal).
