# ServerPilot MVP threat model

This lightweight model is updated when an active issue introduces a real trust
boundary. It currently covers user authentication, one-time Agent installation tokens,
Agent registration, revocable Agent credentials, Windows DPAPI-protected local Agent
credential storage, authenticated heartbeat/polling loops, persisted ServerInstance
process configuration, owner-created ServerCommand history and Agent-authenticated
command claim/result updates, the Windows Agent local-process supervisor and the staged,
idempotent command executor, persisted process identity, restart reconciliation and
offline ServerInstance semantics.

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
  | response includes that command's stored ServerInstance process configuration
  | bounded failure details are stored; raw messages are not logged or returned to users

Registered Agent -> ASP.NET Core API -> PostgreSQL: process-state reconciliation
  | paginated assignments and reports are scoped to the credential Agent ID
  | persisted state: reported status + PID + process start time + server receipt time
  | user view derives Unreachable from Agent heartbeat and preserves the stale snapshot

Registered Agent runtime
  | per-request Agent credential; sequential heartbeat, claim and reconciliation loops
  | bounded retry only for network, 408, 429 and 5xx failures
  v
ASP.NET Core API

Agent command executor
  | Running transition -> one local action -> actual-state inspection -> state report
  | terminal report follows; transient retries reuse the cached local outcome
  v
Process supervisor boundary
  | stored native .exe configuration only; no shell or command-payload executable
  | PID + UTC start time + executable path + process name checked before every signal
  v
Windows process: bounded graceful wait, then explicit logged forced termination
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
| Lost claim response or overlapping Agent polls | Claim locks the Agent row, re-delivers its existing `Claimed`/`Running` command, and a partial unique index permits only one such command per Agent | Lease expiry, abandonment and reassignment are deferred |
| Temporary API outage creates a request storm | Heartbeat and polling retry only transient network/408/429/5xx failures up to three times with bounded exponential delay and jitter, then return to their normal cadence | Repeated outages remain visible in Agent logs; no central circuit breaker exists in MVP |
| Revoked credential or invalid Agent contract retries forever | `401`/`403`, unexpected `4xx` and malformed claim responses are classified as non-retryable; both loops stop and the Agent host exits visibly | Operator must correct configuration or register again after credential revocation |
| One Agent processes multiple claimed commands concurrently | The polling loop keeps one staged work item until its terminal report succeeds and does not claim another command meanwhile | Multi-command parallelism is intentionally outside the MVP |
| Agent claims or updates another Agent's command | Claim route ID must equal the credential identity; every command update includes authenticated `agent_id`, and missing/foreign IDs both return `404` | A stolen credential retains its Agent authority until revoked |
| Replayed, forged or clock-regressed command transition | Conditional updates require the exact predecessor state and nondecreasing timestamps; matching duplicates are idempotent and conflicts return `409` | API host clock synchronization remains an operational dependency |
| Lost state or terminal result response repeats a process side effect | The work item records the verified local outcome before state and terminal reporting; later transient retries send only cached reports | An Agent crash before any durable state report can leave an ambiguous external process that is not auto-adopted |
| Unknown or expanded command type reaches local execution | Claim deserialization accepts exact `StartServer` and `StopServer` values only; unsupported values stop at the protocol boundary | A future command type requires an explicit contract and executor decision |
| Agent error discloses local details or exhausts storage | Failure code/message are required, trimmed and bounded; logs omit both details and user history omits the raw message | The Agent must use stable error codes and avoid unnecessary sensitive detail |
| Command history disappears with its ServerInstance | ServerInstance deletion requires no persisted command history and otherwise returns `409` | Retention and archival policy are outside the MVP |
| Local paths or launch arguments disclosed broadly | List/history responses and structured logs exclude paths and arguments; full configuration is returned only to the owner and the authenticated target Agent with its claimed command | The owner and target Agent legitimately need the configuration and must protect their credentials |
| Path traversal or an API-side path check targets the wrong machine | API validates bounded Windows/UNC path shape; Agent repeats safe absolute-path checks and verifies local executable/working-directory existence immediately before launch | Symlink/reparse-point policy is not expanded beyond the exact executable identity check in the MVP |
| Stored configuration invokes a shell or script | The supervisor accepts a matching native `.exe` only and uses `UseShellExecute = false`; it never invokes `cmd.exe`, PowerShell or command-payload paths | Purpose-built game launchers require a separate bounded decision; `.bat` is rejected today |
| Reused or stale PID terminates an unrelated process | PostgreSQL persists PID plus process start time; after restart the supervisor also matches executable path and normalized process name before adopting or signalling it | A process without a previously persisted complete identity is intentionally not auto-adopted |
| Agent reports state for another Agent's server | Assignment routes require the credential Agent ID to equal the route, and state writes filter by both Agent ID and ServerInstance ID under a row lock | A stolen credential retains authority over its own assigned servers until revoked |
| Offline Agent fabricates a current stopped/running state | User reads derive `Unreachable` from heartbeat freshness while retaining `ReportedStatus`, PID and report time as stale data; no offline job overwrites process state | The last snapshot can remain stale until the Agent reconnects |
| Process inspection failure is mistaken for stopped | Access denied and other inspection failures produce no state report; only a verified missing/mismatched previously Running identity becomes `Crashed` | Repeated inspection failures remain an operational alert, not a definitive state |
| Command execution races periodic reconciliation | Both operations share one Agent-side gate, and successful Start/Stop reports verified state before the terminal command result | Reconciliation is intentionally sequential in the MVP |
| Agent and API clocks differ | API receipt time orders reports; Agent process start time is identity data and is not ordered against the API clock | Operators still need sane clocks for diagnostics and command timestamps |
| Hung process blocks Agent command execution indefinitely | Graceful and forced waits have separate bounds; the executor converts a final timeout into a safe bounded command failure | Long-running command cancellation policy remains minimal in the MVP |
| Active process configuration removed during management | Owner and inactive-state predicates are combined in one conditional delete; active states return `409` | A future command/state transition must handle a deleted inactive instance by verifying existence atomically |
| Stored process configuration changes after a command is created | Process-critical updates return `409` while the process or a command is active; update and command creation serialize on the ServerInstance row | Configuration revisions and snapshots remain deferred |
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
- Persist at most one `Claimed` or `Running` ServerCommand for an Agent and re-deliver it
  before claiming another command.
- Retry only transient Agent API failures with bounded delay; stop rather than retry an
  authentication, configuration or contract failure indefinitely.
- Do not overlap iterations of one Agent loop or claim another command while an in-memory
  command is reserved for sequential processing.
- Record a local process outcome before terminal reporting and never repeat that process
  action merely because its `/complete` or `/fail` response was lost.
- Persist process state only from the authenticated target Agent, scoped by both Agent and
  ServerInstance IDs; user commands never assert actual process state.
- Require PID plus process start time for `Running`; clear both for `Stopped` and `Crashed`.
- Derive `Unreachable` from Agent heartbeat freshness without overwriting the last reported
  process state.
- Do not begin command polling after Agent startup until assignment reconciliation succeeds.
- Scope every Agent command transition by both command ID and authenticated Agent ID.
- Reject invalid command transitions; accept only explicitly defined idempotent duplicates.
- Never return a raw Agent command failure message to the user API.
- Never log Agent failure details or credentials.
- Never expose ServerInstance executable paths, working directories or arguments in a list response or structured log.
- A user cannot delete a ServerInstance while its persisted process state is active.
- The Agent may execute only a stored ServerInstance configuration, never a command path or arguments supplied directly by a command request.
- Return stored process configuration only to its owner or the authenticated target Agent
  as part of that Agent's claimed command.
- Never pass stored process configuration to a shell, PowerShell or `UseShellExecute`.
- Never signal a PID until its start time, executable path and process name match the tracked identity.
- Use HTTPS outside local development.
