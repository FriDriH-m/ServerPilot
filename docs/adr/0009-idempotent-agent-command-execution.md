# ADR 0009: Idempotent Agent command execution

## Context

Issue #29 connects authenticated PostgreSQL command delivery to the Windows process
supervisor. HTTP responses can be lost after the API accepts a transition, and a process
action can finish before its terminal result reaches the API. Retrying the whole
operation after every network failure could start a duplicate process or repeat a stop,
while accepting executable input in a command payload would bypass the stored
ServerInstance trust boundary.

## Decision

- Project the target ServerInstance executable path, arguments, working directory and
  process name from PostgreSQL alongside the atomically claimed command. The values come
  from the stored row joined by both ServerInstance ID and authenticated Agent ID; they
  are not accepted from a command request or a second mutable lookup.
- Deserialize only exact `StartServer` and `StopServer` Agent command types. An unknown
  type is a protocol/configuration failure and never reaches a process supervisor.
- Process one in-memory work item at a time through explicit stages: acknowledge
  `Running`, execute the local action once, verify actual process state through the
  supervisor, then report `Completed` or `Failed`.
- Cache the typed local outcome on the work item before attempting the terminal API
  report. A transient `/complete` or `/fail` outage retries only that report; it does not
  repeat the process action. API transitions remain independently idempotent.
- Reuse one supervisor per ServerInstance for the Agent lifetime. Reject a different
  process-critical configuration for an already managed instance rather than switching
  the identity behind a running process.
- Carry Command ID and Correlation ID in structured Agent logs and send the command
  Correlation ID as `X-Correlation-ID` on transition requests. Failure reports contain a
  bounded stable code and a generic safe message, never a path, argument or exception.

## Alternatives considered

- Claim only a ServerInstance ID and fetch configuration separately: rejected because a
  second request creates a configuration race and extra failure point after claim.
- Put executable path or arguments in the user command payload: rejected because it
  turns command delivery into an arbitrary execution channel.
- Retry the complete orchestration after any API failure: rejected because a lost
  terminal response would repeat a local side effect.
- Add a durable Agent execution journal now: deferred; #30 adds persisted actual-state
  reconciliation and safe process rediscovery without a general command journal.
- Update ServerInstance status from the user command or command transition alone:
  rejected because requested state is not proof of actual process state; #30 adds the
  Agent-authenticated actual-state contract.

## Consequences

- The target Agent receives full process configuration for its own claimed command. It
  remains absent from user list responses, command history and structured logs.
- Process actions are not automatically retried within one Agent lifetime. Only their
  already-recorded result is retried after a reporting outage.
- ADR 0010 extends the flow with Agent-authenticated state reporting, persisted full process
  identity, restart rediscovery and unexpected-exit detection. A process without a complete
  previously persisted identity is still not auto-adopted.
- A configuration validation or process error leaves an auditable `Failed` command with
  safe details instead of leaking local host information.

## Verification evidence

- Unit tests cover ordered start/stop orchestration, state verification, safe process
  failures, unsupported command rejection and lost completion-response replay without a
  second process action.
- HTTP client tests cover typed claim deserialization, Agent authentication and
  correlation propagation on result requests.
- PostgreSQL integration tests verify that new and recovery claims include the stored
  configuration selected for the authenticated Agent.
- `eng/verify.ps1` builds, formats, runs unit and PostgreSQL integration tests, checks
  migration drift and builds the API Docker image.
