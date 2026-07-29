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

- Claim the oldest pending command for an Agent by `(created_at, id)` in one PostgreSQL
  statement using `SELECT ... FOR UPDATE SKIP LOCKED` inside a data-modifying CTE and
  `UPDATE ... RETURNING`.
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

- One command cannot be claimed twice, while concurrent polls may claim different pending
  commands for the same Agent.
- Locked rows are skipped instead of extending a poll request, keeping claim transactions
  short and avoiding a blocking queue.
- A successfully claimed command currently has no lease or automatic recovery if an Agent
  disappears. Timeout and retry policy remain separate MVP work.
- Result idempotency has an explicit payload rule; an Agent retry can distinguish an
  accepted duplicate from a conflicting result.

## Verification evidence

- PostgreSQL integration tests issue concurrent claim requests for one pending command
  and verify one `200`, one `204`, status `Claimed` and `AttemptCount == 1`.
- Integration tests verify oldest-first selection, cross-Agent `404`, conditional state
  transitions, duplicate progress/results and bounded failure details.
- Unit tests verify failure-detail normalization and rejection before persistence.
