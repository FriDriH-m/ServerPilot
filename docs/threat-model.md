# ServerPilot MVP threat model

This lightweight model is updated when an active issue introduces a real trust
boundary. It currently covers user authentication, one-time Agent installation tokens,
Agent registration, revocable Agent credentials, Windows DPAPI-protected local Agent
credential storage, persisted ServerInstance process configuration, owner-created
ServerCommand history and Agent-authenticated command claim/result updates. The
local-process execution boundary will be expanded when command execution is implemented.

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

Unregistered Agent
  | HTTPS: raw one-time installation token
  v
ASP.NET Core API -> PostgreSQL: atomic consume + Agent creation
  | HTTPS response once: raw Agent credential
  v
Registered Agent
  | Authorization: Agent <credential>
  v
ASP.NET Core API -> PostgreSQL: credential-hash lookup + revocation state
  | authenticated heartbeat: server UTC only
  v
PostgreSQL: monotonic last_seen_at

Registered Agent
  | current Windows user + DPAPI CurrentUser protection
  v
%LOCALAPPDATA%\ServerPilot\agent-credential.dat: encrypted Agent ID, scheme and credential

User/client -> ASP.NET Core API -> PostgreSQL: owner-scoped Agent metadata query
  | response: safe metadata + derived Online/Offline state

User/client -> ASP.NET Core API -> PostgreSQL: owner-scoped ServerInstance configuration
  | list response: safe metadata only; full paths/arguments only for the owner
  | stored configuration: target Agent + absolute path shape + bare process name

User/client -> ASP.NET Core API -> PostgreSQL: owner-scoped ServerCommand request/history
  | only StartServer or StopServer; one Pending/Claimed/Running command per ServerInstance
  | response: command state, timestamps and safe error code; no raw Agent failure message

Registered Agent -> ASP.NET Core API -> PostgreSQL: claim/progress/result
  | Agent ID comes from the credential; route IDs must match it exactly
  | oldest Pending command is claimed by one atomic PostgreSQL statement
  | bounded failure details are stored; raw messages are not logged or returned to users

Future flow: API -> authenticated Agent -> stored allow-listed ServerInstance process operations
```

The client/API and API/PostgreSQL transitions are trust boundaries. The JWT signing
key is process configuration and never crosses to PostgreSQL or source control. The
raw installation token crosses the API boundary only in its creation response and is
not recoverable from persisted data. The raw Agent credential is likewise returned once,
uses a separate authentication scheme and is represented in PostgreSQL only by its hash.

## Active threats and controls

| Threat | Current control | Remaining limitation |
|---|---|---|
| Password disclosure | Framework password hashing; request bodies and credentials are not logged | TLS termination must be configured for non-local deployment |
| Account enumeration during login | Same 401 Problem Details and dummy password verification for unknown/wrong credentials | Registration still reports an existing email by design |
| Duplicate accounts under concurrency | Unique PostgreSQL index on normalized email; specific constraint handling | Normalization is intentionally limited to trim + invariant case folding |
| Forged or modified access token | HMAC-SHA256 signature plus issuer, audience, lifetime, algorithm and subject validation | Symmetric key rotation is not implemented in the MVP |
| Stolen access token | Short 30-minute lifetime; tokens are not persisted or logged | No immediate revocation or refresh-token flow |
| Client-supplied ownership identifier | Authenticated user ID comes from the validated `sub` claim; Agent and ServerInstance queries scope through that owner | Future owned resources must preserve the same owner-scoped query pattern |
| Known or committed deployment secret | Example secrets are empty, Compose requires explicit values, and startup rejects the former public JWT placeholder | Operators must generate, rotate and protect strong deployment-specific values |
| Credential response cached | JWT, raw installation-token and raw Agent-credential responses use `Cache-Control: no-store` | Clients must still protect credentials after receipt |
| Online credential guessing or token flooding | Fixed-window limits protect anonymous authentication and authenticated token endpoints; active token count and list size are bounded | Distributed deployments will need a shared or upstream limiter if per-process limits are insufficient |
| Predictable installation credential | 256 random bits from .NET `RandomNumberGenerator`; a GUID or user identifier is never used as the credential | Entropy depends on the operating system CSPRNG |
| Installation token disclosed by database or list API | PostgreSQL stores only a SHA-256 hash; list responses contain metadata only; raw value is returned once | A client that loses the response must revoke or wait for expiry and create another token |
| Stolen installation token | 15-minute default lifetime, configurable bounded expiry and explicit revocation | The credential is bearer-only, so HTTPS and client-side protection remain mandatory |
| Cross-user token or Agent access | Installation-token operations, Agent reads and credential revocation are scoped to the JWT subject; foreign IDs return 404 | Every future owned resource must preserve the same owner-scoped query pattern |
| Reuse of expired, revoked or used installation token | Agent creation and conditional token consumption share one transaction; inactive/concurrently consumed tokens update zero rows | PostgreSQL remains the single registration authority |
| Agent credential disclosed by database | 256 random bits; only a SHA-256 hash is persisted and indexed | The raw bearer credential is stored only on the registered Windows Agent |
| Local Agent credential file is copied or read by another user | The payload is encrypted by Windows DPAPI with `CurrentUser` scope and stored under the current user's local application-data directory; atomic replacement avoids a partially written credential | Malware or an interactive process running as the same Windows user can still use DPAPI; protect that account and revoke/re-register if compromise is suspected |
| User/Agent principal confusion | User endpoints use the default Bearer JWT scheme; Agent endpoints require an explicit Agent policy and claim | Every future heartbeat/command endpoint must select the Agent policy |
| Stolen or revoked Agent credential | Authentication checks the hash and `credential_revoked_at` in PostgreSQL on every request; the owner can revoke credentials | Credentials do not expire or rotate automatically in the MVP; an in-flight request is not cancelled |
| Agent submits heartbeat for another Agent | The route ID must equal the exact Agent ID resolved by authentication; mismatches return 404 and do not write | A credential still acts as its own Agent until revoked |
| Client clock forges or regresses liveness | Heartbeat has no client timestamp; server UTC conditionally advances `last_seen_at`, and PostgreSQL rejects values before registration | API host clock synchronization is an operational dependency |
| Persisted availability becomes stale | `Online`/`Offline` is derived during reads from `last_seen_at` and a validated threshold; no boolean or scheduled status write exists | Status is a recent-contact signal, not proof that the local process is healthy |
| Cross-user ServerInstance access | Create verifies ownership of the target Agent; list/get/update/delete scope through the Agent owner and foreign IDs return `404` | Future command endpoints must preserve the same ServerInstance ownership path |
| Cross-user command creation or history access | Command creation and history queries scope the ServerInstance through its Agent owner; missing and foreign IDs both return `404` | Future command-by-ID APIs must preserve the same owner scope |
| Conflicting Start/Stop requests under concurrency | PostgreSQL partial unique index permits one `Pending`, `Claimed` or `Running` command per ServerInstance; its named unique violation becomes `409` | Cancellation and retry policy are deferred to Agent command processing |
| Two Agent polls claim the same command | One data-modifying statement uses `FOR UPDATE SKIP LOCKED` and `UPDATE ... RETURNING`; concurrent PostgreSQL tests prove one claim and one attempt | Claimed-command lease expiry and automatic retry are deferred |
| Agent claims or updates another Agent's command | Claim route ID must equal the credential identity; every command update includes authenticated `agent_id`, and missing/foreign IDs both return `404` | A stolen credential retains its Agent authority until revoked |
| Replayed or forged command transition | Conditional updates require the exact predecessor state; matching duplicates are idempotent and conflicting repeats return `409` | Automated timeout/retry policy is not part of this issue |
| Agent error discloses local details or exhausts storage | Failure code/message are required, trimmed and bounded; logs omit both details and user history omits the raw message | The Agent must use stable error codes and avoid unnecessary sensitive detail |
| Command history disappears with its ServerInstance | ServerInstance deletion requires no persisted command history and otherwise returns `409` | Retention and archival policy are outside the MVP |
| Local paths or launch arguments disclosed broadly | List responses and structured logs exclude paths and arguments; full configuration is returned only to the owning user | The owner can retrieve their configuration, so clients must protect their own API credentials |
| Path traversal or an API-side path check targets the wrong machine | API rejects `.`/`..` and device-path segments, validates bounded Windows/UNC path shape only, and does not inspect a remote host | A future Agent must validate the stored configuration before process execution |
| Active process configuration removed during management | Owner and inactive-state predicates are combined in one conditional delete; active states return `409` | A future command/state transition must handle a deleted inactive instance by verifying existence atomically |
| Security operation has no audit trail | User/Agent registration, login, token operations and credential revocation emit structured events with identifiers but no credential values | Full API/Agent correlation remains in issue #31 |

## Security invariants

- Never log passwords, password hashes, bearer tokens or signing keys.
- Never persist or return the raw Agent installation token after its creation response.
- Never persist, log or return the raw Agent credential after registration.
- Persist the Agent credential only in the current Windows user's DPAPI-protected local
  storage; never commit it or keep it in `appsettings.json`.
- Never authorize ownership from a user ID supplied in a request body or route.
- Only accept JWTs matching the configured issuer, audience and signing algorithm.
- Consume an installation token and create its Agent in one transaction.
- Never accept an Agent credential as a user JWT or a user JWT as Agent authentication.
- A heartbeat may update only the Agent identity resolved from its credential.
- Never trust a client-provided timestamp for Agent availability.
- Scope every Agent read to the authenticated user before projecting metadata.
- Scope every ServerInstance operation to the authenticated owner of its target Agent.
- Scope every ServerCommand request and history query to the authenticated owner of its ServerInstance.
- Persist at most one active (`Pending`, `Claimed` or `Running`) ServerCommand for a ServerInstance.
- Claim a pending ServerCommand at most once, atomically in PostgreSQL, for its assigned Agent.
- Scope every Agent command transition by both command ID and authenticated Agent ID.
- Reject invalid command transitions; accept only explicitly defined idempotent duplicates.
- Never return a raw Agent command failure message to the user API.
- Never log Agent failure details or credentials.
- Never expose ServerInstance executable paths, working directories or arguments in a list response or structured log.
- A user cannot delete a ServerInstance while its persisted process state is active.
- The future Agent may execute only a stored ServerInstance configuration, never a command path or arguments supplied directly by a command request.
- Use HTTPS outside local development.
