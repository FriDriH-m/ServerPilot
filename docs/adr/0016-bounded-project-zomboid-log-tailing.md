# ADR 0016: Bounded Project Zomboid log tailing

## Context

Issue #40 lets an owner inspect recent local game-server output through the browser. Local
file access expands the Windows Agent trust boundary: accepting a path from the browser or a
generic backend payload would create an arbitrary file-read API. Sending whole growing files,
persisting every line or polling without backpressure would also create unbounded memory,
storage and request pressure.

The Project Zomboid profile already owns one canonical live log path,
`<DataDirectory>\console.txt`. Agent process reconciliation is sequential, scoped to assigned
ServerInstances and already reports bounded operational state through an authenticated API
request.

## Decision

- Support logs only for the `ProjectZomboid` profile in this issue. Derive `console.txt` from
  the validated stored data directory on both sides; the browser supplies only a ServerInstance
  ID and an opaque cursor. `Generic` remains explicitly unsupported until a future profile owns
  a concrete log-source contract.
- Send a SHA-256 source identifier with the Agent assignment and require it on every log report.
  The API recomputes it from the current row, so an in-flight report from an older data-directory
  configuration is rejected without exposing the path in the log contract.
- Read with `FileShare.ReadWrite | FileShare.Delete`, reject a reparse-point data directory or
  file, and transfer only complete UTF-8 lines. Strip BOM/terminal control sequences and redact
  known password, token, secret, API-key and Authorization values before they leave the Agent.
  Unknown secrets remain an operator responsibility.
- Track one in-memory stream ID, byte offset, creation time and bounded prefix fingerprint per
  assigned ServerInstance. Truncation, replacement, Agent restart or fingerprint change creates
  a reset stream. A partial line waits for its newline; an overlong line is discarded rather
  than growing memory without bound.
- Read at most once per five seconds, at most 16 KiB and 200 complete lines per chunk. Use the
  existing sequential reconciliation request and retry policy; no second overlapping Agent loop
  or user-triggered local read is introduced.
- Validate and append Agent chunks under the existing ServerInstance PostgreSQL row lock. Store
  only the latest 32 KiB / 400-line window plus the current stream/offset and last delta. Exact
  retry cursors refresh report time without appending duplicate content. Configuration-source
  changes clear the old snapshot.
- Expose logs only through the owner-scoped `GET /api/server-instances/{id}/logs` endpoint under
  the existing authenticated-user rate limit. A matching cursor returns no lines, the immediately
  following chunk returns a delta, and a missed/rotated cursor returns the bounded full window as
  a reset. Staleness uses API receipt time and Agent heartbeat, never an Agent clock.
- Keep at most 400 lines in React memory. Pause stops browser log requests, resume follows the
  next bounded update, filtering is local, and missing/unavailable/offline states remain visible.

## Alternatives considered

- Accept an arbitrary path or directory from the browser: rejected because it would expose the
  Agent as a remote file browser and make path authorization dependent on untrusted input.
- Add generic per-instance log paths now: deferred because the Generic profile has no stable
  encoding, rotation, ownership or redaction contract; the existing Project Zomboid profile is
  a complete bounded use case.
- Upload the full tail window every reconciliation: rejected because unchanged large payloads
  waste bandwidth and cannot distinguish retry from new output.
- Persist every line or use Loki: rejected because #40 is a recent operational view, not durable
  retention or centralized observability.
- Let the API call the Agent on demand: rejected because the Agent uses outbound polling and need
  not expose an inbound network service.

## Consequences

- The feature cannot browse `Logs` archives or arbitrary files; it follows only the canonical
  Project Zomboid console log.
- PostgreSQL and browser storage remain constant per ServerInstance. A slow/disconnected browser
  recovers from the latest bounded reset and cannot request historical lines that have fallen out
  of the window.
- Reparse checks reduce link-based escape risk, while local ACLs remain the primary boundary;
  a local administrator or the Agent service identity can still replace readable files.
- Redaction is intentionally conservative and cannot prove that every game/mod secret format is
  covered. Operators must avoid writing secrets to game logs and restrict owner/API access.
- A future server profile must define its own fixed allowed source, encoding and rotation rules
  before it can opt in.

## Verification evidence

- Agent unit tests cover complete UTF-8 lines, BOM handling, incremental append, throttling,
  truncation/rotation, missing files and known-secret redaction.
- Domain tests cover source binding, cursor gaps, exact retry idempotency, bounded windows,
  rotation reset and configuration-source clearing.
- PostgreSQL integration tests cover authenticated Agent reporting, owner-only reads, delta/reset
  cursors, duplicate retry and invalid-source rejection.
- Web tests cover bounded merge/reset behavior, pause/resume, filtering, stale/unsupported states
  and rate-limit-compatible non-overlapping polling.
