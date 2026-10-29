# ADR-0006 — Cache-aside with `IMemoryCache` on the redirect path

- **Status:** Accepted
- **Phase:** 2a (cache)

## Context

Redirect is the hot path: one request per click, and the same links get clicked repeatedly. Every redirect needs `originalUrl` and `expiresAt` to answer, and must increment `clickCount`. Caching the whole entity would make the counter stale and turn a cheap read into a write-conflict problem.

## Decision

Use `IMemoryCache` cache-aside on `ShortUrlService.GetOriginalAsync`:

- key per `shortCode`, TTL `ShortUrl:CacheTtlSeconds` (default 300);
- the cached value holds only `originalUrl` + `expiresAt`, never `clickCount`;
- the click increment runs against the database with `ExecuteUpdateAsync`, outside the cached value;
- `DELETE` removes the entry; an expired entry found in the cache is removed and reported as expired;
- the cache is an in-process implementation detail, injected as `IMemoryCache` — swapping it for a distributed cache would keep the same seam.

## Consequences

- Positive: the hot `SELECT` disappears on repeat clicks, while stats stay exact because the counter is always read from the database.
- Positive: expiry is enforced correctly even for cached entries, since `expiresAt` travels with the value.
- Negative: `IMemoryCache` is per process, so multiple instances would each keep their own copy and invalidation would not cross instances. Acceptable while the demo runs a single Render instance; Redis is the stretch path.
- Negative: two round trips per redirect on a cache miss (read + increment), which is the price of correctness for the counter.
