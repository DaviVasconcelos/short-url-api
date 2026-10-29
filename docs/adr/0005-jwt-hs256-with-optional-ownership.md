# ADR-0005 — JWT HS256 with optional ownership on create

- **Status:** Accepted
- **Phase:** 3a (schema), 3b (auth), 3c (ownership)

## Context

The API started with anonymous creation only. Adding accounts raises a design question: should authentication become mandatory? Making it mandatory would break the simplest use case of a shortener (paste a URL, get a short URL) and would turn the demo into a walled garden. Adding it optionally, however, creates two classes of records and needs a rule for each operation.

## Decision

- Authentication is **JWT HS256**, 60-minute expiry, claims `sub` (user id), `email` and `jti`. Passwords hashed with `PasswordHasher<User>`, e-mails normalized (trim + lowercase) before insert and lookup.
- `Jwt:Key` is absent from `appsettings.json`. In Development an ephemeral 32-byte key is generated and a warning is logged; outside Development the app fails fast unless `Jwt__Key` is set with at least 32 bytes.
- `POST /api/urls` accepts an **optional** Bearer token. A valid token sets `OwnerId`; a missing header creates an anonymous link; a present-but-invalid header is rejected with `401` before body validation.
- `GET /api/urls` and `DELETE /api/urls/{code}` require the owner. Anonymous links are not listable and not deletable: `401` without a token, `403` for a different user or for an anonymous link, `404` when the code does not exist.
- Redirects (`GET /{code}`) and stats (`GET /api/urls/{code}`) stay public.
- `ShortUrl.OwnerId` is nullable with `OnDelete(SetNull)`, so deleting a user orphans links instead of cascading.

## Consequences

- Positive: the funnel stays short — anonymous use works without any account.
- Positive: ownership is enforced in one place (`ShortUrlService`), with the controller only extracting the `sub` claim.
- Positive: no secrets in the repository; the ephemeral key makes local runs frictionless and production runs fail loudly when misconfigured.
- Negative: tokens die with the process when no stable key is configured, so Development restarts invalidate sessions.
- Negative: no refresh tokens, roles or password reset. Out of scope by design (see GLOSSARY).
- Negative: an anonymous link is permanently orphaned — it can never be claimed or deleted through the API.
