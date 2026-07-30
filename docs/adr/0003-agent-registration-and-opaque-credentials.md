# ADR 0003: Agent registration and opaque credentials

## Context

Issue #20 turns an unregistered Windows process into an authenticated Agent. The
one-time installation token and the permanent Agent credential cross different trust
boundaries and must not be interchangeable with a user's JWT. Concurrent registration
attempts must never create two Agents from one token, and credential revocation must
take effect without waiting for an access-token lifetime.

## Decision

- Register through `POST /api/agents/register` with the installation token and bounded
  Agent metadata: name, machine name, operating system and Agent version.
- Read the candidate token owner, then conditionally set `used_at` and insert the Agent
  in one PostgreSQL transaction. The conditional update requires the exact token ID,
  hash and owner, an active lifetime, and null used/revoked timestamps. Zero updated
  rows means the credential lost a race or is inactive; a failed Agent insert rolls
  back token use.
- Generate a distinct Agent credential from 32 random bytes with the `spac_` prefix.
  Return it once with `Cache-Control: no-store`; persist only the lowercase SHA-256 hash.
- Authenticate Agents through `Authorization: Agent <credential>`. Keep the user JWT
  default as `Authorization: Bearer <jwt>` and require an explicit Agent-only policy on
  Agent endpoints, preventing either principal type from being accepted accidentally.
- Resolve every Agent credential against PostgreSQL and project the exact Agent ID into
  the authenticated principal. Do not issue a self-contained Agent JWT in the MVP.
- Store `credential_revoked_at` on the Agent. Owner-scoped conditional revocation is
  idempotent, returns `404` for missing/foreign Agents, and blocks subsequent requests
  because authentication consults PostgreSQL each time.
- Accept heartbeat only when the route Agent ID equals the ID resolved from the Agent
  credential. Use the API server's UTC timestamp rather than a client-provided value,
  and conditionally advance `last_seen_at` so delayed requests cannot move it backward.
- Derive `Online` or `Offline` during owner-scoped reads from nullable `last_seen_at` and
  a validated configuration threshold. The exact threshold boundary remains `Online`;
  no persisted availability boolean or background status writer is used.
- Log registration, rejection, revocation and heartbeat authorization failures with
  correlation/resource identifiers but never log installation tokens, Agent credentials
  or their hashes.
- On its first console start, the Windows Agent validates typed API, identity and future
  loop-interval configuration before any hosted background service starts. It sends the
  one-time installation token only when no local credential exists, then persists the
  returned Agent ID, `Agent` scheme and credential atomically in the current user's
  `%LOCALAPPDATA%\ServerPilot\agent-credential.dat` file protected with Windows DPAPI
  (`CurrentUser` scope). Later starts reuse that credential and do not need or log the
  installation token.

## Alternatives considered

- Reuse user JWTs: rejected because a machine principal must not inherit user identity
  or authorization semantics.
- Issue a long-lived Agent JWT: rejected because immediate revocation would require a
  database lookup or blacklist anyway while adding signing/rotation complexity.
- Use short-lived Agent JWT plus refresh credentials: deferred until measurements show
  that a database lookup per Agent request is a bottleneck.
- Use mutual TLS: explicitly outside the MVP and substantially more complex to install
  and rotate on Windows hosts.
- Persist the raw credential: rejected because a database disclosure would expose every
  usable Agent identity.
- Accept an Agent ID or timestamp from the heartbeat body: rejected because identity
  comes from authentication and client clocks are not an availability authority.
- Persist an `is_online` flag maintained by a background job: rejected because it adds
  writes and scheduling failure modes for state that can be derived deterministically.
- Store the credential in `appsettings.json` or an environment variable permanently:
  rejected because both are routinely copied into diagnostics, deployment manifests or
  process inspection output. The one-time installation token is accepted only from
  external configuration on first start.
- Use a plaintext file: rejected because local file disclosure would immediately expose
  a long-lived bearer credential. Windows DPAPI is available without adding a separate
  secret-management service and is sufficient for this Windows-only MVP Agent.

## Consequences

- Agent credentials are high-entropy, long-lived bearer secrets. HTTPS and secure local
  storage remain mandatory; Agent-side persistence belongs to issue #26.
- Revocation is immediate for requests that start after the database update commits.
  An already-authenticated in-flight request is not cancelled.
- Authentication performs one indexed PostgreSQL lookup per Agent request. This is a
  deliberate MVP trade-off for simple, reliable revocation.
- Credential rotation is performed by registering a replacement Agent and revoking the
  old one until an explicit rotation flow is justified.
- Used installation-token metadata may still be removed by its retention policy because
  the Agent's ownership and credential state are stored independently.
- Availability reads use one configurable threshold and one captured server timestamp,
  so every item in a list is evaluated consistently. Clock synchronization remains an
  operational requirement for the API host.
- The credential is bound to the Windows identity that registered the Agent. Moving the
  Agent to a different user or machine requires a new installation token and registration.
  ADR 0013 applies the same DPAPI `CurrentUser` rule to the dedicated Windows Service
  identity and restricted ProgramData storage. DPAPI does not protect against malicious
  code running as that same identity.

## Verification evidence

- Domain tests cover Agent metadata/hash invariants and idempotent credential revocation.
- PostgreSQL integration tests prove one successful Agent under concurrent registration,
  rejection of expired/revoked/used tokens, hash-only persistence and schema constraints.
- API integration tests prove user/Agent scheme separation, exact Agent identity,
  owner-scoped revocation, rejection after revocation and absence of secrets in logs.
- Heartbeat integration tests prove exact authenticated-Agent matching, server UTC,
  monotonic concurrent updates, owner-only queries and both sides of the availability
  threshold boundary.
- Agent unit tests prove typed configuration validation plus first-run registration,
  durable credential handoff and existing-credential reuse without an installation token.
