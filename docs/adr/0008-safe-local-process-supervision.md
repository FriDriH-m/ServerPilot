# ADR 0008: Safe local process supervision

## Context

Issue #28 introduces the first component that can affect an operating-system process on
the Agent host. Stored `ServerInstance` values cross from an owner-controlled API into a
Windows execution boundary. A stale or reused PID, an executable path that resolves to a
script, or a stop request racing with process exit could otherwise start arbitrary code
or terminate an unrelated process.

The supervisor remains independent from command orchestration. Issue #29 maps an
authenticated, claimed command and its stored ServerInstance configuration to this
component through a per-ServerInstance registry.

## Decision

- Accept only bounded, absolute Windows drive or UNC paths without device namespaces or
  `.`/`..` segments. Verify that the executable and working directory exist locally
  immediately before launch.
- In the MVP supervisor, accept only a native `.exe` whose file name matches the stored
  process name. Start it with `UseShellExecute = false` and `CreateNoWindow = true`.
  Arguments are passed to that executable, never to `cmd.exe`, PowerShell or a shell.
- Serialize start, inspect and stop operations for one ServerInstance. A second start
  returns `AlreadyRunning` while the tracked identity is still valid.
- Identify a launched process by PID, UTC start time, executable path and normalized
  process name. Re-read and compare all identity fields immediately before every stop
  signal. A missing process is stopped; a mismatch is a stale PID and is never signalled.
- Attempt `CloseMainWindow` only when the process has a window. Wait for a bounded
  graceful timeout, then log the ServerInstance ID, PID and timeout before an explicit
  forced termination. The forced wait is bounded separately.
- Return typed operation status and failure codes from the platform boundary instead of
  exposing raw operating-system exceptions or local paths in errors and logs.

## Alternatives considered

- Execute `.bat` through `cmd.exe`: rejected because a stored argument string would then
  become shell input and enlarge the MVP execution boundary. A future bounded game
  profile may introduce a purpose-built launcher after a separate security decision.
- Match only PID or process name: rejected because Windows can reuse PIDs and unrelated
  processes can share a name.
- Keep a `Process` object as the sole identity: rejected because Agent restarts and
  process lifetime races make an object reference insufficient evidence.
- Build a generic shell/OS automation abstraction: rejected because the MVP supports
  only StartServer and StopServer against stored configuration.

## Consequences

- API path-shape validation remains host-independent; local existence and process
  identity checks live only in the Windows Agent.
- Existing `.bat`-shaped ServerInstance data is rejected safely by this supervisor. The
  Project Zomboid profile remains deferred and must not silently introduce shell input.
- The supervisor tracks the full identity in memory. Restart rediscovery and persisted
  process-state reconciliation remain issue #30.
- A console process has no graceful window signal, so it reaches the logged, bounded
  forced fallback.

## Verification evidence

- Unit tests cover configuration rejection, duplicate start prevention, running and
  stopped decisions, stale PID detection, and graceful versus forced stop policy.
- A harmless executable fixture verifies real start, inspection and stop behavior on
  Windows without invoking a shell.
- `eng/verify.ps1` builds, formats, runs unit and PostgreSQL integration tests, checks
  migration drift and builds the API Docker image.
