# ADR 0010: Agent-authoritative process-state reconciliation

## Context

Issue #30 must make persisted ServerInstance state describe the inspected Windows process,
not the last user request. The API can restart independently, an Agent can restart while a
managed process keeps running, process IDs can be reused, and an offline Agent cannot prove
that a process is either running or stopped. Command execution and periodic inspection can
also race unless they share an ordering boundary.

## Decision

- The authenticated target Agent is authoritative for the reported process state. User
  command creation and command lifecycle transitions never update ServerInstance process
  state.
- PostgreSQL stores the last reported state, PID, process start time and server receipt
  time. `Running` requires PID plus start time; `Stopped` and `Crashed` clear both identity
  fields. Database constraints preserve these combinations.
- Agent-only paginated assignment reads are scoped by the authenticated Agent ID and return
  stored process configuration plus the last persisted identity. Agent-only state reports
  are scoped by both Agent ID and ServerInstance ID.
- A ServerInstance row lock serializes configuration changes and state reports. Domain
  transitions accept only explicit stable states and reject malformed, stale or impossible
  reports. Repeated reports are safe and refresh observation time without replaying a
  process action.
- Before command polling becomes eligible after startup, reconciliation must successfully
  load assignments. A persisted `Running` identity seeds the supervisor; PID, start time,
  executable path and process name must all match before the process is rediscovered or
  signalled.
- Command execution and periodic reconciliation share one Agent-side gate. Successful
  StartServer/StopServer records its verified `Running`/`Stopped` state before the terminal
  command result; transient report retry reuses the cached outcome.
- Missing or mismatched identity after a previously reported `Running` state becomes
  `Crashed`. Inspection failures such as access denied do not fabricate `Stopped`.
- User reads derive `Unreachable` when the owning Agent is offline. The last reported state,
  PID and report time remain available as explicitly stale data and are not overwritten.

## Alternatives considered

- Update process state from Start/Stop command creation or completion: rejected because a
  request/result is not proof of current local state and cannot detect later exits.
- Persist `Unreachable` in ServerInstance rows: rejected because availability is derived
  from heartbeat freshness and a scheduled write would itself become stale.
- Rediscover by PID or process name alone: rejected because PID reuse or an unrelated
  same-name process could cause ServerPilot to adopt or stop the wrong process.
- Scan all local processes after Agent restart: rejected for the MVP because configuration
  matching without the persisted start identity is ambiguous.
- Run command execution and reconciliation without coordination: rejected because an
  inspection between a successful stop and its state report could incorrectly classify the
  intentional exit as a crash.

## Consequences

- API restart is harmless because the reported state and complete identity are persisted.
- Agent restart safely recovers a process only when a previously persisted complete identity
  still matches; otherwise it reports `Crashed` and never signals the candidate.
- The process start timestamp comes from the Agent host while report receipt time comes from
  the API host. They are deliberately not ordered against each other, so clock skew cannot
  invalidate an otherwise safe identity.
- A process started outside ServerPilot, or one whose initial state report never reached the
  API before Agent failure, is not adopted automatically.
- Reconciliation remains sequential and paginated. Parallel inspection and automatic
  restart are outside the MVP.

## Verification evidence

- Domain tests cover identity requirements and explicit `Running -> Crashed` transitions.
- Agent tests cover persisted-identity seeding, restart rediscovery, stale/missing process
  crash detection, no fabricated stopped state, and state-report retry without a repeated
  process action.
- HTTP client tests cover assignment parsing and authenticated state reports.
- PostgreSQL integration tests cover Agent ownership, persisted PID/start time/status,
  invalid reports, online/offline user projections and `Unreachable` stale semantics.
- `eng/verify.ps1` validates build, formatting, unit tests, PostgreSQL integration tests,
  migration drift, Compose configuration and the API image.
