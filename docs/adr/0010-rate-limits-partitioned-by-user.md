# ADR-0010 — Rate limits are partitioned by user when authenticated

- **Status:** Accepted
- **Phase:** 3e (hardening)

## Context

Rate limiting was keyed by `RemoteIpAddress` alone. That is the right default for anonymous traffic, but it left two holes for authenticated callers:

- **the token was not the unit of cost.** A caller with a valid token could rotate IPs and get a fresh 30 requests/min bucket every time, so the limit only punished people who stayed on one network;
- **the listing endpoint had no policy at all.** `GET /api/urls` (added in 3d) could be paged through without any throttle, and `DELETE` was protected only by ownership.

There was also a trap waiting for the fix: `UseRateLimiter()` ran *before* `UseAuthentication()`, so `HttpContext.User` was always empty inside the policy. Keying on the user id without reordering the middleware would have silently kept partitioning by IP for everyone — a bug that looks like it works.

## Decision

- Key authenticated requests by `user:{id}` (the `sub` claim) and anonymous ones by `ip:{ip}`, falling back to `ip:unknown` when there is no remote address (the `TestServer` case).
- Keep `auth` (register/login) keyed by **IP**: there is no identity yet, and keying by the submitted email would let an attacker spread attempts across addresses while staying under the limit.
- Policies and defaults: `create-url` 30/min, `list-urls` 60/min, `delete-url` 30/min, `auth` 10/min. All four live in `RateLimitingOptions` and are validated at startup, so tuning does not need a rebuild.
- Move `UseRateLimiter()` to after `UseAuthentication()` and before `UseAuthorization()`.
- While adding the `400` for out-of-range `expiresInDays`, fix the error responses: `BadRequest(Problem(...))` wrapped an `ObjectResult` inside another `ObjectResult`, so the body was the serialization of the wrapper instead of `ProblemDetails`. Return `Problem(...)` directly — it already carries the status code.

## Consequences

- Positive: a token can no longer reset its quota by changing networks, and listing/delete are bounded.
- Positive: the anonymous path is unchanged in behavior and still limited — the tests pin both branches.
- Positive: `expiresInDays` above 3650 now returns `400` instead of reaching `DateTime.AddDays` and producing a `500`.
- Negative: the anonymous bucket is per IP, so everyone behind a shared NAT (office, mobile carrier) shares one quota.
- Negative: `UseRateLimiter` now runs after JWT validation, so a rejected request pays token validation first. Negligible next to the database work it saves.
- Negative: limits are per process. A multi-instance deployment would allow the full quota on each instance; a distributed limiter needs Redis and is out of scope.
- Negative: the numbers are fixed-window, so a client can burst up to 2× the limit around a window boundary. Accepted for a demo; sliding windows would be the upgrade.
