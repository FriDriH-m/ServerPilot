# ServerPilot MVP threat model

This lightweight model is updated when an active issue introduces a real trust
boundary. It currently covers user authentication from issue #18 and Agent installation
tokens from issue #19; long-lived Agent credentials and the local-process boundary will
be expanded by their implementation issues.

## Data flow and trust boundaries

```text
User/client
  | HTTPS: email + password, then bearer JWT
  v
ASP.NET Core API
  | HTTPS response once: raw Agent installation token
  | EF Core: normalized identity + password/token hashes + token state
  v
PostgreSQL

Future unregistered Agent
  | HTTPS: raw one-time installation token
  v
ASP.NET Core API -> PostgreSQL: atomic consume + Agent creation (issue #20)

Future flow: API -> authenticated Agent -> allow-listed local process operations
```

The client/API and API/PostgreSQL transitions are trust boundaries. The JWT signing
key is process configuration and never crosses to PostgreSQL or source control. The
raw installation token crosses the API boundary only in its creation response and is
not recoverable from persisted data.

## Active threats and controls

| Threat | Current control | Remaining limitation |
|---|---|---|
| Password disclosure | Framework password hashing; request bodies and credentials are not logged | TLS termination must be configured for non-local deployment |
| Account enumeration during login | Same 401 Problem Details and dummy password verification for unknown/wrong credentials | Registration still reports an existing email by design |
| Duplicate accounts under concurrency | Unique PostgreSQL index on normalized email; specific constraint handling | Normalization is intentionally limited to trim + invariant case folding |
| Forged or modified access token | HMAC-SHA256 signature plus issuer, audience, lifetime, algorithm and subject validation | Symmetric key rotation is not implemented in the MVP |
| Stolen access token | Short 30-minute lifetime; tokens are not persisted or logged | No immediate revocation or refresh-token flow |
| Client-supplied ownership identifier | Authenticated user ID comes from the validated `sub` claim | Resource ownership enforcement begins when owned resources are implemented |
| Known or committed deployment secret | Example secrets are empty, Compose requires explicit values, and startup rejects the former public JWT placeholder | Operators must generate, rotate and protect strong deployment-specific values |
| Credential response cached | JWT and raw installation-token responses use `Cache-Control: no-store` | Clients must still protect credentials after receipt |
| Online credential guessing or token flooding | Fixed-window limits protect anonymous authentication and authenticated token endpoints; active token count and list size are bounded | Distributed deployments will need a shared or upstream limiter if per-process limits are insufficient |
| Predictable installation credential | 256 random bits from .NET `RandomNumberGenerator`; a GUID or user identifier is never used as the credential | Entropy depends on the operating system CSPRNG |
| Installation token disclosed by database or list API | PostgreSQL stores only a SHA-256 hash; list responses contain metadata only; raw value is returned once | A client that loses the response must revoke or wait for expiry and create another token |
| Stolen installation token | 15-minute default lifetime, configurable bounded expiry and explicit revocation | The credential is bearer-only, so HTTPS and client-side protection remain mandatory |
| Cross-user token access | List/revoke queries are scoped to the authenticated JWT subject; foreign IDs return 404 | Agent registration ownership will be bound during issue #20 |
| Reuse of expired, revoked or used token | Domain transitions reject inactive states; PostgreSQL validates state timestamps; revocation is a conditional atomic update | Atomic concurrent consume and Agent creation must be implemented and tested in issue #20 |
| Security operation has no audit trail | Registration, login, token creation and token revocation emit structured events with correlation and resource identifiers but no credential values | Full API/Agent correlation remains in issue #31 |

## Security invariants

- Never log passwords, password hashes, bearer tokens or signing keys.
- Never persist or return the raw Agent installation token after its creation response.
- Never authorize ownership from a user ID supplied in a request body or route.
- Only accept JWTs matching the configured issuer, audience and signing algorithm.
- Consume an installation token atomically when Agent registration is implemented.
- Use HTTPS outside local development.
