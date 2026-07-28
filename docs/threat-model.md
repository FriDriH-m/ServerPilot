# ServerPilot MVP threat model

This lightweight model is updated when an active issue introduces a real trust
boundary. It currently covers user authentication added by issue #18; Agent and
local-process credentials will be expanded by their implementation issues.

## Data flow and trust boundaries

```text
User/client
  | HTTPS: email + password, then bearer JWT
  v
ASP.NET Core API
  | EF Core: normalized identity + password hash
  v
PostgreSQL

Future flow: API -> authenticated Agent -> allow-listed local process operations
```

The client/API and API/PostgreSQL transitions are trust boundaries. The JWT signing
key is process configuration and never crosses to PostgreSQL or source control.

## Active threats and controls

| Threat | Control in issue #18 | Remaining limitation |
|---|---|---|
| Password disclosure | Framework password hashing; request bodies and credentials are not logged | TLS termination must be configured for non-local deployment |
| Account enumeration during login | Same 401 Problem Details and dummy password verification for unknown/wrong credentials | Registration still reports an existing email by design |
| Duplicate accounts under concurrency | Unique PostgreSQL index on normalized email; specific constraint handling | Normalization is intentionally limited to trim + invariant case folding |
| Forged or modified access token | HMAC-SHA256 signature plus issuer, audience, lifetime, algorithm and subject validation | Symmetric key rotation is not implemented in the MVP |
| Stolen access token | Short 30-minute lifetime; tokens are not persisted or logged | No immediate revocation or refresh-token flow |
| Client-supplied ownership identifier | Authenticated user ID comes from the validated `sub` claim | Resource ownership enforcement begins when owned resources are implemented |
| Secret committed to source | Signing key is required through environment/configuration and absent from appsettings | Operators must generate and protect a strong deployment-specific value |

## Security invariants

- Never log passwords, password hashes, bearer tokens or signing keys.
- Never authorize ownership from a user ID supplied in a request body or route.
- Only accept JWTs matching the configured issuer, audience and signing algorithm.
- Use HTTPS outside local development.
