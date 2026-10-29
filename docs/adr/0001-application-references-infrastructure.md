# ADR-0001 — `Application` references `Infrastructure`

- **Status:** Accepted
- **Phase:** 0a (solution setup)

## Context

The classic Clean Architecture direction puts interfaces in the inner layer and implementations in the outer one, so `Application` never knows about `Infrastructure`. That costs a port per dependency, an extra mapping layer, and DI wiring for every `DbContext` operation.

This project is a single bounded context with one persistence technology per run. `ShortUrlService` and `AuthService` are the only consumers of `AppDbContext`, and the swap point that actually matters — SQLite versus PostgreSQL — is a different `DbContext` subclass, not a different database technology.

## Decision

`Application` references `Infrastructure`, not the reverse. `Api` references `Application` and `Infrastructure.Postgres`, and both `Application` and `Infrastructure` depend on `Domain`.

Interfaces that `Infrastructure` must implement live in `ShortUrl.Domain/Abstractions` (for example `ICodeGenerator`); `Infrastructure` is never allowed to reference `Application`.

## Consequences

- Positive: one service class per concern, no repository layer, no mapping boilerplate. Services are tested directly against a real `AppDbContext` (SQLite in-memory), which exercises LINQ and `ExecuteUpdateAsync` for real.
- Positive: provider independence is achieved with `AddDbContext<AppDbContext, PostgresAppDbContext>`, so services never learn which database is running.
- Negative: the arrow reads "backwards" to anyone expecting the textbook layout, so this ADR exists to answer that question.
- Negative: the "`Infrastructure` must not reference `Application`" rule is enforced by review only — there is no architecture test guarding it.
