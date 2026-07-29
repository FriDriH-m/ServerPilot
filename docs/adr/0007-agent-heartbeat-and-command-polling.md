# ADR 0007: Agent heartbeat and command polling

## Context

Issue #27 turns the bootstrapped Windows Agent into a continuously authenticated API
client. HTTP delivery can fail, credentials can be revoked, and a lost response can
re-deliver a claimed command. The Agent must remain available during short API outages
without creating request storms, processing concurrent commands or treating a revoked
credential as a transient condition.

## Decision

- Reuse the DPAPI-protected Agent credential only in per-request
  `Authorization: Agent <credential>` headers for typed heartbeat and command-claim
  clients. Do not put the credential in an `HttpClient` default header or logs.
- Run heartbeat and command polling as independent sequential loops. Each loop awaits
  its request and its delay before beginning another iteration, so a slow request cannot
  overlap with a later iteration of the same loop.
- Treat network failures, request timeouts, `429` and `5xx` responses as transient. An
  operation makes at most three retries after its initial attempt, using 1/2/4-second
  exponential delays with bounded jitter; after exhaustion the normal loop delay applies.
- Treat `401`/`403` as authentication failures and other unexpected `4xx` responses or
  malformed claim payloads as configuration/protocol failures. Log their kind and stop
  the Agent host instead of retrying indefinitely.
- Hold the first `Claimed` or recovery-delivered command in one in-memory slot. The
  polling loop makes no further claim while that slot is occupied. Process transition and
  execution are intentionally deferred to #29 after the #28 process supervisor exists.
- Log Agent ID for loop lifecycle and failure events; include command ID and correlation
  ID when a command is reserved. Never log credentials, authorization headers, paths,
  arguments or raw failure payloads.

## Alternatives considered

- Polly or another resilience package: rejected for the MVP because the required retry
  policy is small, explicit and fully testable with framework facilities.
- One shared loop for heartbeat and claims: rejected because a blocked claim would delay
  liveness and make the two configured intervals meaningless.
- Retry every non-success response forever: rejected because revoked credentials and
  broken configuration would create noisy request loops and hide an operator action.
- Claim repeatedly before an executor exists: rejected because the API deliberately
  re-delivers the active command and the MVP guarantees one active command per Agent.

## Consequences

- A temporary API outage costs at most four attempts in one loop cycle before the Agent
  returns to its configured cadence. Recovery remains eventual without a tight loop.
- Credential revocation and configuration faults require operator action or a restart
  after correction; the Agent exits visibly rather than silently reconnecting forever.
- A command can remain `Claimed` until #29 implements progress/result processing. On a
  restart the API returns it as `Recovery`, preserving the one-command invariant.
- The Agent has no additional durability or lease-expiry mechanism yet; those are not a
  substitute for idempotent process execution in #29.

## Verification evidence

- Unit tests cover typed authorization headers, claim response validation and HTTP
  failure classification.
- Unit tests cover bounded transient retry, cancellation during retry delay, sequential
  loop scheduling, one reserved command, and stopping both loops on authentication
  failure.
- `eng/verify.ps1` builds, formats, runs unit and PostgreSQL integration tests, checks
  migration drift and builds the API Docker image.
