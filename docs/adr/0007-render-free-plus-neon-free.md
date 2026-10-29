# ADR-0007 — Render Free + Neon Free for the public demo

- **Status:** Accepted
- **Phase:** 4a (container readiness), 4b (deploy)

## Context

The project needs a live URL that a recruiter can open, at **zero cost and without a credit card**. Requirements: run the Docker image, provide a managed PostgreSQL, run migrations on startup, expose environment secrets, and survive a cold start without being restarted by an over-eager health check.

Alternatives considered: Fly.io (credit card required, more moving parts), Azure App Service free tier (card, region constraints, Linux tier limits), a VPS (always cost, always maintenance), and running only locally (defeats the purpose).

## Decision

- **Render Free web service**, Docker runtime, region `virginia`, declared in `render.yaml` (Blueprint) rather than clicked together in the dashboard.
- **Neon Free PostgreSQL**, region `us-east-1`, connected through the **direct** host (no `-pooler`) so startup migrations do not go through PgBouncer in transaction mode.
- Health check path is `/health/live`, not `/health`: a waking database must not trigger a restart loop.
- `autoDeployTrigger: checksPass`, so a deploy only happens after the GitHub Actions workflow is green.
- Secrets: `ConnectionStrings__Postgres` with `sync: false` (filled in the dashboard), `Jwt__Key` with `generateValue: true` (stable across deploys). `Database__Provider=Postgres` and `Swagger__Enabled=true` are plain env vars.
- `ShortUrl__BaseUrl` is intentionally unset, which exercises the request-derived fallback and proves `ForwardedHeaders` works behind Render's proxy.
- Cold start is accepted, not worked around: no keep-alive pings, because 750 free hours per month are enough for a demo but not for a 24/7 poll.

## Consequences

- Positive: total cost is $0 with no card, and the whole setup is reproducible from `render.yaml` plus three dashboard values.
- Positive: the runbook (`docs/deploy.md`) documents the smoke test, the rollback and the cold-start behavior, so the deploy is repeatable without this conversation.
- Negative: the first request after ~15 minutes of inactivity can take up to a minute while Render and Neon wake up. Documented in the README, and the reason the demo is not used as an uptime monitor.
- Negative: a single free instance and an in-process cache rule out horizontal scaling and make Redis a real (stretch) requirement before scaling out.
- Negative: `render.yaml` cannot hold the connection string, so one manual step remains — by design, since the alternative would be a secret in git.
