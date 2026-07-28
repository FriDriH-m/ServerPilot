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
- Log registration, rejection and revocation with correlation/resource identifiers but
  never log installation tokens, Agent credentials or their hashes.

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

## Verification evidence

- Domain tests cover Agent metadata/hash invariants and idempotent credential revocation.
- PostgreSQL integration tests prove one successful Agent under concurrent registration,
  rejection of expired/revoked/used tokens, hash-only persistence and schema constraints.
- API integration tests prove user/Agent scheme separation, exact Agent identity,
  owner-scoped revocation, rejection after revocation and absence of secrets in logs.
