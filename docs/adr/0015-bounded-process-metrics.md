# ADR 0015: Bounded process metrics collection and display

## Context

Issue #39 must show useful CPU, memory and uptime data for a managed server process without
introducing Prometheus, a time-series store or another overlapping Agent loop. Process metrics
cross the local-process and Agent-authentication boundaries, and an unbounded sample table at
the existing ten-second reconciliation cadence would create 8,640 rows per server per day.

The process-state reconciliation loop already verifies the complete persisted process identity,
is serialized with command execution and has bounded retry/backpressure semantics. The owner
details endpoint and management dashboard already expose the authoritative server state.

## Decision

- Capture cumulative processor time and working-set bytes during the existing identity-checked
  process inspection. Do not inspect a machine-wide process name or accept a PID from a metric
  request independently of the stored process identity.
- Sample during the configured `ProcessReconciliationInterval` loop. The loop remains sequential,
  so a slow request delays the next sample instead of creating overlapping work or a request storm.
- Calculate normalized CPU percentage from two consecutive cumulative CPU samples, elapsed
  monotonic time and logical processor count. The first sample has no CPU percentage; memory and
  uptime are immediately available. Reset the baseline when process identity changes.
- Send metrics only as part of the Agent-authenticated process-state report. The API validates
  finite CPU values in the inclusive 0-100 range and non-negative memory/uptime values, assigns
  its own receipt timestamp and updates state plus metrics under the existing ServerInstance row
  lock. The metric receipt time may precede a later state-only report for the same identity but
  can never be later than the current state receipt time.
- Persist only the latest snapshot on `ServerInstance`. Clear it when the verified process stops,
  crashes or changes identity without a new sample. A PostgreSQL constraint keeps the metric
  fields internally consistent with a `Running` state and its report timestamp.
- Return metrics only from the owner-scoped ServerInstance details endpoint. Mark them stale when
  the Agent is offline, the effective server state is not Running, or the snapshot age exceeds
  the existing Agent offline threshold.
- Keep at most 30 distinct, non-stale snapshots in React memory for a short CPU sparkline. Reload,
  navigation away from the server and process staleness discard this transient history.

## Alternatives considered

- Persist every sample in PostgreSQL: rejected because #39 needs a current operational view, not
  long-term analytics, and the row growth is disproportionate to the feature.
- Add Prometheus now: deferred to the observability stage; it adds deployment and retention policy
  decisions outside this issue.
- Create a separate high-frequency Agent metrics loop: rejected because reconciliation already
  owns verified inspection and supplies non-overlap, cancellation and bounded retry behavior.
- Calculate CPU from a single instantaneous operating-system value: rejected because the .NET
  process API exposes cumulative CPU time and a delta gives deterministic, testable semantics.
- Persist the browser sparkline: rejected because a bounded, short-lived chart is sufficient for
  the management view and avoids another retention contract.

## Consequences

- PostgreSQL storage remains constant per ServerInstance, while the browser chart represents only
  the current tab's recent observations and is not an audit or monitoring history.
- CPU appears after two successful inspections; memory and uptime appear on the first report.
- Reconciliation cadence controls metric freshness. Backpressure reduces sampling frequency rather
  than accumulating work.
- CPU is normalized across logical processors and capped at 100 percent for a whole managed process;
  it is not a per-core raw percentage.
- Agent/API clocks cannot forge freshness because only API receipt time is persisted.
- Future durable telemetry or alerting requires a separate observability decision and migration.

## Verification evidence

- Agent unit tests cover the first sample, CPU delta calculation, memory/uptime and identity reset.
- Domain and API tests cover valid samples, invalid bounds, idempotent reports, clearing and stale
  behavior.
- PostgreSQL integration tests cover migration mapping, ownership-preserving reports and the latest
  snapshot contract.
- Web tests cover metric rendering, stale presentation, unit formatting and the bounded 30-sample
  history.
- The repository verification script exercises build, formatting, unit/integration tests, migration
  drift, Compose startup and the Windows process fixture.
