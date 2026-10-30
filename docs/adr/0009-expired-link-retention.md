# ADR-0009 — Expired links are retained for 30 days before cleanup

- **Status:** Accepted
- **Phase:** 6a + 6b (expired-link cleanup)

## Context

Nothing ever removed a row from `ShortUrls`. A link that expired answered `410 Gone` forever, because the row was still there, and the table grew without bound — the only path that deleted rows was the owner-only `DELETE`.

Purging expired links changes an observable contract. Today an expired link answers `410`; once the row is gone the same code answers `404`. Clients that distinguish "existed but expired" (`410`) from "never existed" (`404`) lose that distinction the moment the row disappears.

The cleanup also has to run somewhere. Doing it inline on the redirect path would put a write-heavy delete in the hot path; doing it in a separate process would add deployment surface to a single-container demo.

## Decision

Run a `BackgroundService` (`ExpiredLinkCleanupService`) inside the API, backed by `ExpiredLinkCleaner`:

- it deletes rows whose `ExpiresAt` is older than **30 days past expiration** (`Cleanup:RetentionDays`), not rows that are merely expired;
- so `410` keeps working for a month after expiry, and the row (and its click count) survives long enough for anyone to inspect it;
- it runs once at startup and then every `Cleanup:IntervalMinutes` (default 60);
- it deletes in batches of `Cleanup:BatchSize` (default 500) until a batch comes back incomplete — `ExecuteDeleteAsync` with `Take` does not translate reliably on both providers, so it selects ids first and deletes by `Contains`;
- `ExpiresAt` gets an index on both providers, because the filter is `ExpiresAt < cutoff` ordered by `ExpiresAt`;
- `Cleanup:Enabled` is `false` by default, so development, tests and any container without the flag never delete data by surprise. Production sets `Cleanup__Enabled=true` in `render.yaml`;
- the clock comes from `TimeProvider` (built into .NET 8), so tests are deterministic without sleeping;
- a failed run is logged as an error and retried at the next interval — it never takes the host down.

## Consequences

- Positive: the table stops growing forever, and the delete is bounded per run instead of a single long transaction.
- Positive: `410 Gone` remains accurate for 30 days after expiry, which is the window where a user is most likely to click a stale link.
- Positive: the retention window is configuration, not code — setting `Cleanup:RetentionDays=0` deletes as soon as the link expires, if that is ever preferred.
- Negative: the contract is now time-dependent. `GET /{code}` answers `410` while the row exists and `404` after the cleanup removes it. This is documented in the README and covered by the cleaner tests; a client that needs a permanent `410` would have to keep the rows.
- Negative: the cleanup competes with requests on the free-tier instance. Batches of 500 every hour keep each run short, and the work is idempotent, so a slow run only delays the next one.
- Negative: an in-process `BackgroundService` assumes a single instance (or that concurrent runs are acceptable, which they are: the delete is idempotent). A multi-instance deployment would either tolerate the redundant work or move it to an external scheduler.
