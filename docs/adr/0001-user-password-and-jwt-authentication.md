# ADR 0001: User password and JWT authentication

## Context

Issue #18 introduces the first user trust boundary. ServerPilot needs a minimal
identity that can later own Agents and ServerInstances. Passwords must not be stored
or logged in plain text, access tokens must be validated without a database lookup,
and the signing secret must remain outside source control.

## Decision

- Normalize email by trimming and applying invariant uppercase, while preserving a
  trimmed display email.
- Enforce normalized-email uniqueness with a PostgreSQL unique index and translate
  only that constraint violation into the expected duplicate-registration result.
- Hash passwords with ASP.NET Core Identity `PasswordHasher`; do not implement a
  custom password algorithm.
- Issue 30-minute HMAC-SHA256 JWT access tokens containing `sub`, `email` and `jti`.
- Validate issuer, audience, signature, lifetime, algorithm and a GUID `sub` claim.
- Require a signing key of at least 32 UTF-8 bytes from configuration. Example secrets
  are empty and startup rejects the former public placeholder even though it is long.
- Return the same login error for an unknown email and an incorrect password, and
  perform a dummy hash verification for unknown users.
- Apply registration password policy only when creating an account. Login accepts any
  non-empty bounded value so a later policy change cannot lock out an existing hash.
- Persist a replacement hash when framework verification reports `SuccessRehashNeeded`.
- Rate-limit anonymous authentication by client and mark credential responses
  `Cache-Control: no-store`.

## Alternatives considered

- Full ASP.NET Core Identity: rejected for the MVP because roles, stores, cookies and
  account-management flows are outside scope.
- Custom password hashing: rejected because framework password hashing is safer and
  supports future rehash decisions.
- Database-backed opaque sessions and refresh tokens: deferred because the MVP only
  requires short-lived API access tokens.
- Asymmetric signing or an external identity provider: deferred until deployment or
  integration requirements justify key distribution or federation.

## Consequences

- Revocation is not immediate; a leaked access token remains usable until its short
  expiry. HTTPS is required outside local development.
- Changing issuer, audience or signing key invalidates existing access tokens.
- Email uniqueness is concurrency-safe at the database boundary.
- Later ownership checks can depend on `ICurrentUser.UserId` rather than client input.
- Per-process rate limiting is sufficient for the current single-API MVP; a distributed
  deployment must use a shared or upstream limiter if measurements require it.

## Verification evidence

- Unit tests cover deterministic email canonicalization and normalization.
- PostgreSQL integration tests cover registration, login, password hashing, JWT
  validation, generic invalid-credential errors and concurrent duplicate email.
- Configuration validation rejects missing, short and known example signing keys.
- Tests cover login-policy independence, rehash persistence, no-store responses,
  structured security logs without credentials and rate-limit rejection.
