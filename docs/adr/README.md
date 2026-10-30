# Architecture Decision Records

Decisions that shaped this project, recorded after the fact. Each ADR follows **Context → Decision → Consequences**: what forced a choice, what was chosen, and what it costs.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-application-references-infrastructure.md) | `Application` references `Infrastructure` (inverted dependency) | Accepted |
| [0002](0002-base62-short-codes.md) | Base62 short codes with a unique index and retry | Accepted |
| [0003](0003-302-over-301.md) | `302` instead of `301` on redirects | Accepted |
| [0004](0004-sqlite-dev-postgres-production.md) | SQLite for dev/test, PostgreSQL in production, separate migrations | Accepted |
| [0005](0005-jwt-hs256-with-optional-ownership.md) | JWT HS256 with optional ownership on create | Accepted |
| [0006](0006-cache-aside-with-imemorycache.md) | Cache-aside with `IMemoryCache` on the redirect path | Accepted |
| [0007](0007-render-free-plus-neon-free.md) | Render Free + Neon Free for the public demo | Accepted |
| [0008](0008-root-landing-and-literal-routes.md) | Root landing endpoint and literal-route precedence | Accepted |
| [0009](0009-expired-link-retention.md) | Expired links are retained for 30 days before cleanup | Accepted |
| [0010](0010-rate-limits-partitioned-by-user.md) | Rate limits are partitioned by user when authenticated | Accepted |

ADRs are immutable once accepted: when a decision changes, add a new ADR that supersedes the old one instead of rewriting history.
