# ADR 0006: Atomic PostgreSQL command claiming

## Context

Issue #25 delivers pending `ServerCommand` records to authenticated Agents through HTTP
polling. Multiple poll requests can overlap because of retries, multiple API instances or
accidentally duplicated Agent processes. A read followed by a separate update could let
two callers observe and execute the same pending command.

The same API also accepts progress and terminal results. Those writes must be scoped to
the Agent identity established by its credential and must define replay behavior because
HTTP delivery is not exactly once.

## Decision

- Lock the authenticated Agent row during claim so concurrent polls for that Agent are
  serialized across API instances.
- Return an existing `Claimed` or `Running` command first as a recovery delivery. Only
  when none exists, claim the oldest pending command by `(created_at, id)` using
  `SELECT ... FOR UPDATE SKIP LOCKED`, `UPDATE ... RETURNING`, and mark it as a new
  delivery.
- Enforce at most one `Claimed` or `Running` command per Agent with a PostgreSQL partial
  unique index. Keep the index as the final authority for writers outside the claim path.
- Run the statement as a short autocommit transaction. The existing
  `(agent_id, status, created_at, id)` index supports its selection predicate and order.
- Scope claim selection and every transition update by the authenticated `agent_id`.
  Route mismatches and missing or foreign command IDs return the same `404` response.
- Apply `Claimed -> Running`, `Running -> Completed` and `Running -> Failed` with
  conditional database updates rather than an in-memory lock.
- Treat a repeated start as successful once a start timestamp exists. Treat a repeated
  completion as successful only for `Completed`, and a repeated failure as successful
  only when the normalized failure code and message exactly match the stored result.
  Other state conflicts return `409`.
- Use API server UTC for transition timestamps. Require trimmed, bounded failure details,
  omit the raw failure message from user responses and never log either failure detail.
- Clamp `claimed_at` to `created_at` when the API clock regresses. Add predecessor-time
  predicates to start/complete/fail updates so a regressed clock yields `409` instead of
  violating a database check constraint.

## Alternatives considered

- Read then update in application code: rejected because concurrent callers can read the
  same pending row.
- Process-local lock: rejected because it does not coordinate multiple API instances and
  PostgreSQL is the MVP source of truth.
- Serializable transactions with retry: rejected because row locking with `SKIP LOCKED`
  directly models queue consumption with a shorter and simpler transaction.
- RabbitMQ or another broker: rejected because HTTP polling and PostgreSQL are explicit
  MVP constraints.

## Consequences

- One Agent cannot hold multiple claimed/running commands. Overlapping polls return the
  same command: one `New` delivery and subsequent `Recovery` deliveries.
- A lost claim response is recoverable without a lease or a full Agent polling workflow.
  Timeout, abandonment and reassignment policy remain separate MVP work.
- Result idempotency has an explicit payload rule; an Agent retry can distinguish an
  accepted duplicate from a conflicting result.

## Verification evidence

- PostgreSQL integration tests issue concurrent claim requests and verify one `New` and
  one `Recovery` response for the same command, plus the single-active-command index.
- Integration tests verify oldest-first selection, cross-Agent `404`, conditional state
  transitions, clock regression, duplicate progress/results and bounded failure details.
- Unit tests verify failure-detail normalization and rejection before persistence.
