# ADR-0004 — SQLite for dev/test, PostgreSQL in production

- **Status:** Accepted
- **Phase:** 1a (SQLite), 3a (PostgreSQL)

## Context

Tests and local runs should start in seconds with no external dependency; a portfolio demo should still prove the app works on a real server database. A single provider cannot do both well: SQLite is file-based and dependency-free, PostgreSQL is the one production deployments actually use.

## Decision

- `Database:Provider` selects the provider. `Sqlite` is the default; `Postgres` is opt-in and requires `ConnectionStrings:Postgres`, otherwise startup fails fast.
- `AppDbContext` lives in `ShortUrl.Infrastructure` with the SQLite migrations. `PostgresAppDbContext` lives in `ShortUrl.Infrastructure.Postgres`, derives from `AppDbContext`, and changes only the provider, with its own migration history.
- Every model change ships **two migrations**, one per provider, in the same commit.
- Migrations are applied on startup by `DatabaseMigrationRunner`, with configurable retries (`Database:MigrateAttempts`, default 5; `Database:MigrateDelaySeconds`, default 2).
- The production connection string uses the **direct** Neon host (no `-pooler`): PgBouncer in transaction mode is not a good fit for migrations.

## Consequences

- Positive: the full test suite runs in ~3 seconds on in-memory SQLite, with no container and no network.
- Positive: the same model is validated against a real PostgreSQL server, and provider swapping is a configuration change plus a `DbContext` subclass.
- Positive: deploys need no manual `database update` step.
- Negative: two migration histories must be kept in sync by discipline — nothing fails automatically if a commit forgets one.
- Negative: SQLite and PostgreSQL differ in behavior (case sensitivity of string comparisons, `DateTime` mapping to `timestamp with time zone`, `ExecuteUpdateAsync` support). Tests that rely on case-insensitive matching would pass on one provider and fail on the other, so services normalize explicitly (e-mails are lowercased).
