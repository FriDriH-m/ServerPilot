# ADR 0002: One-time Agent installation tokens

## Context

Issue #19 introduces a temporary bearer credential that crosses the user/API trust
boundary and will later be presented by an unregistered Windows Agent. The token must
be unpredictable, short-lived, owned by one user and impossible to recover from the
database. Revocation and one-time use also need explicit state rules.

## Decision

- Generate 32 random bytes with .NET `RandomNumberGenerator` and encode them as a
  prefixed hexadecimal token. The prefix identifies the credential type but carries
  no authority.
- Return the raw token only from the create operation. Persist and query a lowercase
  SHA-256 hash of the complete raw token; never log either value.
- Default expiry to 15 minutes and allow operators to configure 1–1,440 minutes with
  `AgentInstallationTokens:LifetimeMinutes`.
- Serialize creation per user with a PostgreSQL transaction advisory lock and cap the
  number of unexpired active tokens. Paginate list queries with bounded page/limit
  values and opportunistically remove inactive metadata past a configurable retention
  period when that user creates another token.
- Model `UsedAt` and `RevokedAt` as terminal states. Expired, revoked and already-used
  tokens cannot be used; used tokens cannot be revoked; repeating a revocation is
  idempotent. Revocation uses one conditional PostgreSQL update so concurrent requests
  cannot overwrite the winning timestamp.
- Enforce owner-scoped list and revoke queries from the authenticated JWT subject.
  Return `404` for a missing or foreign token so the endpoint does not disclose its
  existence.
- Enforce canonical lowercase SHA-256 hashes, valid lifetimes, valid terminal-state
  timestamps and mutually exclusive used/revoked states in PostgreSQL. Index
  `(user_id, created_at)` for owner metadata queries.
- Rate-limit authenticated token operations, return raw credentials with
  `Cache-Control: no-store`, and log security events without raw values or hashes.
- When issue #20 adds Agent registration, consume a token atomically in the same
  transaction that creates Agent credentials. A read followed by an unguarded update
  is not sufficient for the one-time guarantee.

## Alternatives considered

- Persist the raw token: rejected because a database disclosure would immediately
  expose usable registration credentials.
- Hash with a password KDF: rejected because tokens have 256 bits of cryptographic
  entropy; SHA-256 provides safe lookup without the latency needed for human passwords.
- Use a GUID as the credential: rejected because an identifier is not an explicit
  cryptographic secret and does not communicate the required entropy.
- Store token state only in an in-memory cache: rejected because PostgreSQL is the MVP
  source of truth and the state must survive restarts.
- Implement Agent registration now: deferred to issue #20 to keep this change focused.

## Consequences

- Losing the raw creation response requires issuing a new token; it cannot be recovered.
- Anyone holding an active raw token can present it, so HTTPS and the short lifetime are
  required controls.
- Revoked and used metadata remains available to its owner for lifecycle visibility.
- Bounded active-token/list limits and inactive-metadata retention prevent a single
  authenticated user from growing the credential set without bound through the API.
- Hash collisions are cryptographically improbable; creation still retries a bounded
  number of times if the database unique constraint reports one.
- The definitive concurrent-consumption proof belongs to issue #20, where consumption
  and Agent creation become one operation.

## Verification evidence

- Domain tests cover expiry boundaries, single use, revocation and invalid transitions.
- PostgreSQL integration tests verify authentication, hash-only persistence, one-time
  raw-token disclosure, owner-scoped bounded listing, active-token caps, concurrent
  creation/revocation and used-token conflicts.
- EF Core migration constraints and indexes enforce the persisted invariants and lookup
  paths described above.
