# ADR-0002 — Base62 short codes with a unique index and retry

- **Status:** Accepted
- **Phase:** 1a (domain + database)

## Context

A shortener needs codes that are short, not enumerable, and safe to put in a URL. Sequential IDs (`/1`, `/2`) leak volume and make every record guessable. Hashing the URL produces long codes and cannot serve duplicate URLs with distinct codes. A database sequence per code needs a round trip before the insert.

## Decision

Generate codes with `ICodeGenerator` (`Base62CodeGenerator`), drawing from the alphabet `A–Z a–z 0–9` with `RandomNumberGenerator.GetString`, default length `7` (`ShortUrl:CodeLength`). Persist `ShortCode` under a unique index and retry the insert up to 3 times (`MaxCodeGenerationAttempts`) when the database reports a duplicate.

Callers may supply a `custom alias` instead, validated against `^[a-zA-Z0-9_-]{3,30}$` and rejected when reserved.

## Consequences

- Positive: 62^7 ≈ 3.5 trillion combinations, so collisions are rare and codes are not enumerable.
- Positive: no coordination between app instances — the unique index is the source of truth, and a duplicate surfaces as a constraint violation, not as a race.
- Positive: the generator is an interface in `Domain`, so tests can inject a deterministic stub.
- Negative: retry is bounded at 3 attempts, so a pathological generator or an exhausted space fails the request with `InvalidOperationException` instead of degrading silently.
- Negative: `RandomNumberGenerator` is slower than a counter. Irrelevant at this scale, but it rules out bulk-insert strategies.
