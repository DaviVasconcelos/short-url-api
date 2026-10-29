# ADR-0008 — Root landing endpoint and literal-route precedence

- **Status:** Accepted
- **Phase:** post-4b (demo polish)

## Context

The README links to the bare host, `https://shorturl-davi.onrender.com`. Opening it returned `404` with an empty body, which looks like a broken deploy even though every real route worked.

The cause is routing, not configuration: the app exposes `POST/GET/DELETE /api/urls`, `POST /api/auth/*`, `GET /{code}` (a single segment), `/health`, `/health/live` and `/swagger`. There is no route for `/`, and `/{code}` requires a segment, so the root never matched anything. Requests without a code — `/` or `/a/b` — cannot be a valid short code either.

Adding a page would mean maintaining a frontend for an API-only project. Doing nothing would leave the most-clicked link in the README broken.

## Decision

Map a minimal-API endpoint after `MapControllers()`:

- with `Swagger:Enabled` on, `GET /` answers `302` to `/swagger`;
- with Swagger off (the production default), it answers `200` with a small JSON index (name, Swagger path, health path, endpoint list);
- the endpoint is marked `ExcludeFromDescription()` so the anonymous response type does not leak into the OpenAPI document.

No separate page, no static files, no changes to `RedirectController`.

## Consequences

- Positive: the bare demo URL is usable in both configurations, and the fix costs ~15 lines.
- Positive: literal routes keep winning over `/{code}`, so `/health` and `/swagger` are unaffected; `/` and `/x` are different segment patterns and never collide.
- Positive: two integration tests pin the behavior — redirect when enabled, JSON index when disabled.
- Negative: with Swagger enabled the root costs one extra hop (`/` → `/swagger` → `/swagger/index.html`), since `UseSwaggerUI` redirects `/swagger` to the `index.html` path. Cosmetic; the stable, readable path is worth it.
- Negative: the JSON index duplicates a slice of the endpoint table already in the README. It is deliberately tiny and only returned when Swagger is unavailable.
